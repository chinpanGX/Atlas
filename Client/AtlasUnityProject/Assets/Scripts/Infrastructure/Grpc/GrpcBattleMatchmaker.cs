using System;
using System.Threading;
using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Grpc
{
    // 待機列に入り、成立するまで状況を一定間隔で確かめる。
    public sealed class GrpcBattleMatchmaker : IBattleMatchmaker
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly MatchmakingApi matchmakingApi;

        public GrpcBattleMatchmaker(MatchmakingApi matchmakingApi)
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
                    var status = await matchmakingApi.GetQueueStatusAsync(new GetQueueStatusRequest());
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
