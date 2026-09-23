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
        private BattleSession? _session;
        private int _slot;

        public async Task<JoinResult> JoinAsync(string battleToken, string matchId)
        {
            if (_session is not null)
            {
                return new JoinResult(JoinResultStatus.AlreadyJoined);
            }

            var token = await tokenValidator.ValidateAsync(battleToken);
            var outcome = await coordinator.JoinAsync(token, matchId, ConnectionId, groupName => Group.AddAsync(groupName));
            if (outcome.Status == JoinResultStatus.Success)
            {
                _session = outcome.Session;
                _slot = outcome.Slot;
            }
            else
            {
                logger.LogInformation("JoinAsync rejected. matchId={MatchId} status={Status}", matchId, outcome.Status);
            }

            return new JoinResult(outcome.Status);
        }

        public Task SubmitSelectionAsync(string[] playerPachimonIds) =>
            coordinator.SubmitSelectionAsync(RequireSession(), _slot, playerPachimonIds);

        public Task SubmitMoveAsync(MoveRequest move) =>
            coordinator.SubmitMoveAsync(RequireSession(), _slot, move);

        public Task SwitchAsync(int partySlot) =>
            coordinator.SwitchAsync(RequireSession(), _slot, partySlot);

        public Task ForfeitAsync() =>
            coordinator.ForfeitAsync(RequireSession(), _slot);

        protected override ValueTask OnConnecting()
        {
            logger.LogInformation("Connecting. connectionId={ConnectionId}", ConnectionId);
            return ValueTask.CompletedTask;
        }

        protected override async ValueTask OnDisconnected()
        {
            logger.LogInformation("Disconnected. connectionId={ConnectionId}", ConnectionId);
            if (_session is not null)
            {
                await coordinator.HandleDisconnectAsync(_session, _slot, ConnectionId);
            }
        }

        private BattleSession RequireSession() =>
            _session ?? throw new ReturnStatusException(StatusCode.FailedPrecondition, "JoinAsync has not succeeded");
    }
}
