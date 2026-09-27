using Atlas.BattleServer.Auth;
using Atlas.BattleServer.Battle;
using Atlas.BattleContracts;
using Grpc.Core;
using MagicOnion;
using MagicOnion.Server.Hubs;

namespace Atlas.BattleServer.Hubs
{
    // 1接続=1インスタンス(公式ChatAppサンプルのChatHubと同じパターン)。対戦の進行ロジックは
    // 接続をまたいで共有するBattleCoordinator(シングルトン)に委譲し、Hubは「この接続がどの対戦の
    // どちら側か」だけを保持する。
    public sealed class BattleHub(
        BattleTokenValidator tokenValidator,
        BattleCoordinator coordinator,
        ILogger<BattleHub> logger)
        : StreamingHubBase<IBattleHub, IBattleHubReceiver>, IBattleHub
    {
        private BattleSession? session;
        private int slot;

        public async Task<JoinResult> JoinAsync(string battleToken, string matchId)
        {
            if (session is not null)
            {
                return new JoinResult(JoinResultStatus.AlreadyJoined);
            }

            var token = await tokenValidator.ValidateAsync(battleToken);
            var outcome = await coordinator.JoinAsync(token, matchId, ConnectionId, groupName => Group.AddAsync(groupName));
            if (outcome.Status == JoinResultStatus.Success)
            {
                session = outcome.Session;
                slot = outcome.Slot;
            }
            else
            {
                logger.LogInformation("JoinAsync rejected. matchId={MatchId} status={Status}", matchId, outcome.Status);
            }

            return new JoinResult(outcome.Status);
        }

        public Task SubmitSelectionAsync(string[] playerPachimonIds) =>
            coordinator.SubmitSelectionAsync(RequireSession(), slot, playerPachimonIds);

        public Task SubmitMoveAsync(MoveRequest move) =>
            coordinator.SubmitMoveAsync(RequireSession(), slot, move);

        public Task SwitchAsync(int partySlot) =>
            coordinator.SwitchAsync(RequireSession(), slot, partySlot);

        public Task ForfeitAsync() =>
            coordinator.ForfeitAsync(RequireSession(), slot);

        protected override ValueTask OnConnecting()
        {
            logger.LogInformation("Connecting. connectionId={ConnectionId}", ConnectionId);
            return ValueTask.CompletedTask;
        }

        protected override async ValueTask OnDisconnected()
        {
            logger.LogInformation("Disconnected. connectionId={ConnectionId}", ConnectionId);
            if (session is not null)
            {
                await coordinator.HandleDisconnectAsync(session, slot, ConnectionId);
            }
        }

        private BattleSession RequireSession() =>
            session ?? throw new ReturnStatusException(StatusCode.FailedPrecondition, "JoinAsync has not succeeded");
    }
}
