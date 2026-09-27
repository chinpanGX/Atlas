using System;
using System.Linq;
using System.Threading;
using Atlas.Application;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Atlas.Infrastructure.Api
{
    // POST /battle/queueで待機列に入り、GET /battle/queue/statusを一定間隔で確認して成立を待つ。
    public sealed class ApiBattleMatchmaker : IBattleMatchmaker
    {
        private const string StatusMatched = "matched";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly BattleApiClient client;
        private readonly AccessTokenRefresher accessTokenRefresher;

        public ApiBattleMatchmaker(string baseUrl, AccessTokenStore accessTokenStore, AccessTokenRefresher accessTokenRefresher)
        {
            client = new BattleApiClient(baseUrl, () => accessTokenStore.CurrentToken);
            this.accessTokenRefresher = accessTokenRefresher;
        }

        public async UniTask<BattleMatch> FindMatchAsync(CancellationToken cancellation)
        {
            await accessTokenRefresher.SendAsync(() => client.JoinQueueAsync());
            try
            {
                while (true)
                {
                    var status = await accessTokenRefresher.SendAsync(() => client.QueueStatusAsync());
                    if (status.Status == StatusMatched)
                    {
                        return new BattleMatch(status.MatchId, status.BattleServer, status.BattleToken);
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
