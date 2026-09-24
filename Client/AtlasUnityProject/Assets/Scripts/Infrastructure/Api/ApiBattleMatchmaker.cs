using System;
using System.Threading;
using Atlas.Application;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZLinq;

namespace Atlas.Infrastructure.Api
{
    // POST /battle/queueで待機列に入り、GET /battle/queue/statusを一定間隔で確認して成立を待つ。
    public sealed class ApiBattleMatchmaker : IBattleMatchmaker
    {
        private const string StatusMatched = "matched";
        // BattleServerは1〜3体の選出を受け付ける(パーティが3体未満ならその数だけ送る)。
        private const int MaxSelectionCount = 3;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly BattleApiClient client;
        private readonly AccessTokenRefresher accessTokenRefresher;
        private readonly IPartyService partyService;

        public ApiBattleMatchmaker(
            string baseUrl, AccessTokenStore accessTokenStore, AccessTokenRefresher accessTokenRefresher,
            IPartyService partyService)
        {
            client = new BattleApiClient(baseUrl, () => accessTokenStore.CurrentToken);
            this.accessTokenRefresher = accessTokenRefresher;
            this.partyService = partyService;
        }

        public async UniTask<BattleMatch> FindMatchAsync(CancellationToken cancellation)
        {
            // パーティの枠番号順に先頭から選出する(design/battle.md「パチモン選出(自動選出)」)。
            var selected = partyService.GetAll()
                .OrderBy(slot => slot.Slot)
                .Take(MaxSelectionCount)
                .Select(slot => slot.PlayerPachimonId)
                .ToArray();

            await accessTokenRefresher.SendAsync(() => client.JoinQueueAsync());
            try
            {
                while (true)
                {
                    var status = await accessTokenRefresher.SendAsync(() => client.QueueStatusAsync());
                    if (status.Status == StatusMatched)
                    {
                        return new BattleMatch(status.MatchId, status.BattleServer, status.BattleToken, selected);
                    }

                    await UniTask.Delay(PollInterval, DelayType.Realtime, cancellationToken: cancellation);
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセル時は待機列から抜ける。抜けられなくても次回の参加で上書きされるため、失敗は握りつぶす。
                try
                {
                    await accessTokenRefresher.SendAsync(() => client.LeaveQueueAsync());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Battle] 待機列からの離脱に失敗しました: {e.Message}");
                }

                throw;
            }
        }
    }
}
