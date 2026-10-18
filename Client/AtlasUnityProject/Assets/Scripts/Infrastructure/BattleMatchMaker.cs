using System;
using System.Threading;
using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    // 待機列に入り、成立するまで状況を一定間隔で確かめる。
    public sealed class BattleMatchMaker : IBattleMatchMaker
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly MatchmakingApi matchmakingApi;

        public BattleMatchMaker(MatchmakingApi matchmakingApi)
        {
            this.matchmakingApi = matchmakingApi;
        }

        public async UniTask<BattleMatch> FindMatchAsync(CancellationToken cancellation)
        {
            var joined = await matchmakingApi.JoinQueueAsync(new JoinQueueRequest());
            if (!joined.IsSuccess)
            {
                return null;
            }

            try
            {
                while (true)
                {
                    // 通信そのものは途中で止められないので、確認の合間でキャンセルを見る
                    cancellation.ThrowIfCancellationRequested();
                    // 待機中はマッチング画面のキャンセルを押せるよう、通信中の表示で操作を塞がない
                    var status = await matchmakingApi.GetQueueStatusAsync(new GetQueueStatusRequest(), notifyActivity: false);
                    if (!status.IsSuccess)
                    {
                        return null;
                    }

                    if (status.Value.Status == QueueStatus.Matched)
                    {
                        return new BattleMatch(status.Value.MatchId, status.Value.BattleServer, status.Value.BattleToken);
                    }

                    await UniTask.Delay(PollInterval, DelayType.Realtime, cancellationToken: cancellation);
                }
            }
            catch (OperationCanceledException)
            {
                // 待機列から抜ける。抜けられなくても、次に参加したときに前の分は捨てられる
                await matchmakingApi.LeaveQueueAsync(new LeaveQueueRequest());
                throw;
            }
        }
    }
}
