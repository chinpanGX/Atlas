using System;
using System.Linq;
using Atlas.Domain;
using Cysharp.Net.Http;
using Cysharp.Threading.Tasks;
using Grpc.Net.Client;
using MagicOnion.Client;
using Contracts = Atlas.BattleContracts;
using BattleCore = Atlas.BattleCore;

namespace Atlas.Infrastructure.Realtime
{
    // BattleServer(MagicOnion StreamingHub)に接続するIBattleConnection。IBattleHubの呼び出しと
    // IBattleHubReceiverの受信を、Domain側の型(Atlas.Domainの同名record)へ詰め替えるだけで、
    // バトルの判定(Atlas.BattleCore)は呼ばない。
    //
    // 受信(IBattleHubReceiver)はMagicOnionの受信スレッドから呼ばれるため、UniTask.Postでメイン
    // スレッドに移してからイベントを発火する(購読側のPresenterはUnityのUIを直接触るため)。
    public sealed class RealtimeBattleConnection : IBattleConnection, Contracts.IBattleHubReceiver, IDisposable
    {
        private readonly string battleServerUrl;
        private GrpcChannel channel;
        private Contracts.IBattleHub hub;

        public event Action<BattleStartPayload> OnMatchStart;
        public event Action<TurnResultPayload> OnTurnResult;
        public event Action<BattleEndPayload> OnBattleEnd;
        public event Action OnOpponentDisconnected;
        public event Action OnOpponentReconnected;

        public RealtimeBattleConnection(string battleServerUrl)
        {
            this.battleServerUrl = battleServerUrl;
        }

        // 接続はJoinAsyncの時点で行う(battleTokenの有効期限が短いため、マッチ成立後すぐに呼ぶ前提)。
        public async UniTask<JoinResult> JoinAsync(string battleToken, string matchId)
        {
            // BattleServerは開発中HTTP/2のみ・非TLSで待ち受けるため(BattleServer/Program.cs参照)、
            // HTTP/2(h2c)で接続できるYetAnotherHttpHandlerを使う。
            channel = GrpcChannel.ForAddress(battleServerUrl, new GrpcChannelOptions
            {
                HttpHandler = new YetAnotherHttpHandler { Http2Only = true },
                DisposeHttpClient = true,
            });
            hub = await StreamingHubClient.ConnectAsync<Contracts.IBattleHub, Contracts.IBattleHubReceiver>(
                channel, this, factoryProvider: MagicOnionGeneratedClientInitializer.StreamingHubClientFactoryProvider);

            var result = await hub.JoinAsync(battleToken, matchId);
            return new JoinResult(ToDomain(result.Status));
        }

        public UniTask SubmitSelectionAsync(string[] playerPachimonIds) =>
            hub.SubmitSelectionAsync(playerPachimonIds).AsUniTask();

        public UniTask SubmitMoveAsync(MoveRequest move) =>
            hub.SubmitMoveAsync(new Contracts.MoveRequest(move.MoveId)).AsUniTask();

        public UniTask SwitchAsync(int partySlot) =>
            hub.SwitchAsync(partySlot).AsUniTask();

        public UniTask ForfeitAsync() =>
            hub.ForfeitAsync().AsUniTask();

        public void Dispose()
        {
            // Hubの切断はサーバー側の切断処理(再接続猶予)を起動するだけなので、完了は待たない。
            hub?.DisposeAsync().AsUniTask().Forget();
            channel?.Dispose();
        }

        void Contracts.IBattleHubReceiver.OnMatchStart(Contracts.BattleStartPayload payload) =>
            UniTask.Post(() => OnMatchStart?.Invoke(ToDomain(payload)));

        void Contracts.IBattleHubReceiver.OnTurnResult(Contracts.TurnResultPayload payload) =>
            UniTask.Post(() => OnTurnResult?.Invoke(ToDomain(payload)));

        void Contracts.IBattleHubReceiver.OnBattleEnd(Contracts.BattleEndPayload payload) =>
            UniTask.Post(() => OnBattleEnd?.Invoke(new BattleEndPayload(payload.WinnerId, ToDomain(payload.Reason))));

        void Contracts.IBattleHubReceiver.OnOpponentDisconnected() =>
            UniTask.Post(() => OnOpponentDisconnected?.Invoke());

        void Contracts.IBattleHubReceiver.OnOpponentReconnected() =>
            UniTask.Post(() => OnOpponentReconnected?.Invoke());

        private static BattleStartPayload ToDomain(Contracts.BattleStartPayload payload) =>
            new(
                ToDomain(payload.Self),
                ToDomain(payload.Opponent),
                payload.TurnTimeLimitSeconds,
                payload.SelfMoves
                    .Select(set => new PachimonMoveSet(set.Moves
                        .Select(m => new MoveState(m.MoveId, m.CurrentPp, m.MaxPp))
                        .ToArray()))
                    .ToArray());

        private static ParticipantSnapshot ToDomain(Contracts.ParticipantSnapshot snapshot) =>
            new(
                snapshot.PlayerId,
                snapshot.SelectedPachimon.Select(s => new PachimonSlot(s.IsRevealed, ToDomain(s.State))).ToArray(),
                snapshot.ActivePachimonIndex);

        // 未公開の枠(相手側)はnull。
        private static PachimonBattleState ToDomain(Contracts.PachimonBattleState state) =>
            state is null
                ? null
                : new PachimonBattleState(state.PlayerPachimonId, state.PachimonId, state.HpPercent, state.IsFainted);

        private static TurnResultPayload ToDomain(Contracts.TurnResultPayload payload) =>
            new(
                payload.TurnNumber,
                payload.Actions.Select(a => new ActionResult(
                    a.PlayerId,
                    ToDomain(a.Type),
                    a.MoveId,
                    a.Hit,
                    a.Critical,
                    ToDomain(a.Effectiveness),
                    a.TargetRemainingHpPercent,
                    a.TargetFainted,
                    a.NewActiveIndex,
                    ToDomain(a.RevealedPachimon))).ToArray(),
                payload.PlayersRequiringForcedSwitch);

        private static JoinResultStatus ToDomain(Contracts.JoinResultStatus status) => status switch
        {
            Contracts.JoinResultStatus.Success => JoinResultStatus.Success,
            Contracts.JoinResultStatus.InvalidToken => JoinResultStatus.InvalidToken,
            Contracts.JoinResultStatus.MatchNotFound => JoinResultStatus.MatchNotFound,
            Contracts.JoinResultStatus.AlreadyJoined => JoinResultStatus.AlreadyJoined,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

        private static ActionType ToDomain(Contracts.ActionType type) => type switch
        {
            Contracts.ActionType.Move => ActionType.Move,
            Contracts.ActionType.Switch => ActionType.Switch,
            Contracts.ActionType.Skip => ActionType.Skip,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

        private static EffectivenessResult ToDomain(BattleCore.EffectivenessResult effectiveness) => effectiveness switch
        {
            BattleCore.EffectivenessResult.Immune => EffectivenessResult.Immune,
            BattleCore.EffectivenessResult.NotVeryEffective => EffectivenessResult.NotVeryEffective,
            BattleCore.EffectivenessResult.Normal => EffectivenessResult.Normal,
            BattleCore.EffectivenessResult.SuperEffective => EffectivenessResult.SuperEffective,
            _ => throw new ArgumentOutOfRangeException(nameof(effectiveness), effectiveness, null),
        };

        private static BattleEndReason ToDomain(BattleCore.BattleEndReason reason) => reason switch
        {
            BattleCore.BattleEndReason.AllFainted => BattleEndReason.AllFainted,
            BattleCore.BattleEndReason.Forfeit => BattleEndReason.Forfeit,
            BattleCore.BattleEndReason.DisconnectTimeout => BattleEndReason.DisconnectTimeout,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }
}
