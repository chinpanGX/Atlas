using System;
using System.Threading;
using Atlas.Domain;
using Cysharp.Net.Http;
using Cysharp.Threading.Tasks;
using Grpc.Net.Client;
using MagicOnion.Client;
using ZLinq;
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
    //
    // 接続が切れたら、同じbattleToken/matchIdで接続と参加をやり直す。サーバーは切断から一定時間(再接続の猶予)
    // 対戦を残し、参加し直した側にOnSelectionStart/OnMatchStartで今の状態を再送する。
    public sealed class RealtimeBattleConnection : IBattleConnection, Contracts.IBattleHubReceiver, IDisposable
    {
        // サーバーの再接続の猶予(60秒)より短くし、猶予が切れて負けが決まる前に諦めて知らせる。
        private static readonly TimeSpan ReconnectTimeLimit = TimeSpan.FromSeconds(50);
        private static readonly TimeSpan[] ReconnectIntervals =
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
        };
        // 1回の接続・参加を待つ長さ。応答が無いまま猶予を使い切らないようにする。
        private static readonly TimeSpan ReconnectAttemptTimeout = TimeSpan.FromSeconds(10);
        // 通信が黙って途切れた(切断が届かない)ときにも気付けるよう、クライアントからハートビートを送る。
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);

        private readonly string battleServerUrl;
        private readonly CancellationTokenSource lifetimeCancellation = new();
        private GrpcChannel channel;
        private Contracts.IBattleHub hub;
        private string battleToken;
        private string matchId;
        private bool disposed;
        // 決着後はサーバーが対戦を片付けるため、切れても参加し直さない。受信スレッドから書く。
        private volatile bool battleEnded;

        public event Action<SelectionStartPayload> OnSelectionStart;
        public event Action<BattleStartPayload> OnMatchStart;
        public event Action<TurnResultPayload> OnTurnResult;
        public event Action<BattleEndPayload> OnBattleEnd;
        public event Action<int> OnTurnStart;
        public event Action OnOpponentDisconnected;
        public event Action OnOpponentReconnected;
        public event Action OnReconnecting;
        public event Action OnReconnected;
        public event Action OnConnectionLost;

        public RealtimeBattleConnection(string battleServerUrl)
        {
            this.battleServerUrl = battleServerUrl;
        }

        // 接続はJoinAsyncの時点で行う(battleTokenの有効期限が短いため、マッチ成立後すぐに呼ぶ前提)。
        public async UniTask<JoinResult> JoinAsync(string battleToken, string matchId)
        {
            this.battleToken = battleToken;
            this.matchId = matchId;
            var (status, joinedHub, joinedChannel) = await ConnectAndJoinAsync(lifetimeCancellation.Token);
            if (status == JoinResultStatus.Success)
            {
                Attach(joinedHub, joinedChannel);
            }

            return new JoinResult(status);
        }

        public UniTask SubmitSelectionAsync(string[] playerPachimonIds) =>
            RequireHub().SubmitSelectionAsync(playerPachimonIds).AsUniTask();

        public UniTask SubmitMoveAsync(MoveRequest move) =>
            RequireHub().SubmitMoveAsync(new Contracts.MoveRequest(move.MoveId)).AsUniTask();

        public UniTask SwitchAsync(int partySlot) =>
            RequireHub().SwitchAsync(partySlot).AsUniTask();

        public UniTask ForfeitAsync() =>
            RequireHub().ForfeitAsync().AsUniTask();

        public UniTask ReadyForTurnAsync() =>
            RequireHub().ReadyForTurnAsync().AsUniTask();

        public void Dispose()
        {
            disposed = true;
            // 参加し直している途中の処理がトークンを使い続けるため、Disposeはしない(タイマーを持たないのでGCで回収される)。
            lifetimeCancellation.Cancel();
            Detach();
        }

        // 参加し直している間は送れない。
        private Contracts.IBattleHub RequireHub() =>
            hub ?? throw new InvalidOperationException("BattleServerに接続していません");

        // 参加に成功した接続だけを返す。失敗した接続はここで閉じる。
        private async UniTask<(JoinResultStatus Status, Contracts.IBattleHub Hub, GrpcChannel Channel)> ConnectAndJoinAsync(
            CancellationToken cancellation)
        {
            // BattleServerは開発中HTTP/2のみ・非TLSで待ち受けるため(BattleServer/Program.cs参照)、
            // HTTP/2(h2c)で接続できるYetAnotherHttpHandlerを使う。
            var newChannel = GrpcChannel.ForAddress(battleServerUrl, new GrpcChannelOptions
            {
                HttpHandler = new YetAnotherHttpHandler { Http2Only = true },
                DisposeHttpClient = true,
            });
            Contracts.IBattleHub newHub = null;
            try
            {
                var options = StreamingHubClientOptions.CreateWithDefault()
                    .WithClientHeartbeatInterval(HeartbeatInterval)
                    .WithClientHeartbeatTimeout(HeartbeatTimeout);
                newHub = await StreamingHubClient.ConnectAsync<Contracts.IBattleHub, Contracts.IBattleHubReceiver>(
                    newChannel, this, options, MagicOnionGeneratedClientInitializer.StreamingHubClientFactoryProvider,
                    cancellation);
                var result = await newHub.JoinAsync(battleToken, matchId);
                var status = ToDomain(result.Status);
                if (status == JoinResultStatus.Success)
                {
                    return (status, newHub, newChannel);
                }

                Close(newHub, newChannel);
                return (status, null, null);
            }
            catch
            {
                Close(newHub, newChannel);
                throw;
            }
        }

        private void Attach(Contracts.IBattleHub joinedHub, GrpcChannel joinedChannel)
        {
            hub = joinedHub;
            channel = joinedChannel;
            WatchDisconnectAsync(joinedHub).Forget();
        }

        private void Detach()
        {
            Close(hub, channel);
            hub = null;
            channel = null;
        }

        private static void Close(Contracts.IBattleHub closingHub, GrpcChannel closingChannel)
        {
            // Hubの切断はサーバー側の切断処理(再接続の猶予)を起動するだけなので、完了は待たない。
            closingHub?.DisposeAsync().AsUniTask().Forget();
            closingChannel?.Dispose();
        }

        private async UniTaskVoid WatchDisconnectAsync(Contracts.IBattleHub watchedHub)
        {
            await watchedHub.WaitForDisconnectAsync();
            await UniTask.SwitchToMainThread();
            // 自分から閉じた接続と、参加し直して入れ替わった古い接続の切断は無視する。
            if (disposed || battleEnded || watchedHub != hub)
            {
                return;
            }

            ReconnectAsync(lifetimeCancellation.Token).Forget();
        }

        private async UniTaskVoid ReconnectAsync(CancellationToken cancellation)
        {
            Detach();
            OnReconnecting?.Invoke();

            var giveUpAt = DateTime.UtcNow + ReconnectTimeLimit;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    attemptCancellation.CancelAfter(ReconnectAttemptTimeout);
                    var (status, joinedHub, joinedChannel) = await ConnectAndJoinAsync(attemptCancellation.Token);
                    await UniTask.SwitchToMainThread();
                    if (disposed)
                    {
                        Close(joinedHub, joinedChannel);
                        return;
                    }

                    if (status == JoinResultStatus.Success)
                    {
                        Attach(joinedHub, joinedChannel);
                        OnReconnected?.Invoke();
                        return;
                    }

                    // AlreadyJoinedはサーバーがまだ前の接続の切断に気付いていないだけなので、待って送り直す。
                    // それ以外(対戦が終わった・トークンが通らない)は、送り直しても参加できない。
                    if (status != JoinResultStatus.AlreadyJoined)
                    {
                        UnityEngine.Debug.LogWarning($"[Battle] 再接続できませんでした: {status}");
                        break;
                    }
                }
                catch (Exception e) when (!cancellation.IsCancellationRequested)
                {
                    UnityEngine.Debug.LogWarning($"[Battle] 再接続に失敗しました({attempt + 1}回目): {e.Message}");
                }
                catch
                {
                    // 自分から閉じた(Dispose)ため、通信が止められた。
                    return;
                }

                await UniTask.SwitchToMainThread();
                if (disposed || battleEnded)
                {
                    return;
                }

                var interval = ReconnectIntervals[Math.Min(attempt, ReconnectIntervals.Length - 1)];
                if (DateTime.UtcNow + interval >= giveUpAt)
                {
                    break;
                }

                if (await UniTask.Delay(interval, DelayType.Realtime, cancellationToken: cancellation)
                        .SuppressCancellationThrow())
                {
                    return;
                }
            }

            if (!disposed && !battleEnded)
            {
                OnConnectionLost?.Invoke();
            }
        }

        void Contracts.IBattleHubReceiver.OnSelectionStart(Contracts.SelectionStartPayload payload) =>
            UniTask.Post(() => OnSelectionStart?.Invoke(ToDomain(payload)));

        void Contracts.IBattleHubReceiver.OnMatchStart(Contracts.BattleStartPayload payload) =>
            UniTask.Post(() => OnMatchStart?.Invoke(ToDomain(payload)));

        void Contracts.IBattleHubReceiver.OnTurnResult(Contracts.TurnResultPayload payload) =>
            UniTask.Post(() => OnTurnResult?.Invoke(ToDomain(payload)));

        void Contracts.IBattleHubReceiver.OnBattleEnd(Contracts.BattleEndPayload payload)
        {
            battleEnded = true;
            UniTask.Post(() => OnBattleEnd?.Invoke(new BattleEndPayload(payload.WinnerId, ToDomain(payload.Reason))));
        }

        void Contracts.IBattleHubReceiver.OnTurnStart(int timeLimitSeconds) =>
            UniTask.Post(() => OnTurnStart?.Invoke(timeLimitSeconds));

        void Contracts.IBattleHubReceiver.OnOpponentDisconnected() =>
            UniTask.Post(() => OnOpponentDisconnected?.Invoke());

        void Contracts.IBattleHubReceiver.OnOpponentReconnected() =>
            UniTask.Post(() => OnOpponentReconnected?.Invoke());

        private static SelectionStartPayload ToDomain(Contracts.SelectionStartPayload payload) =>
            new(
                payload.SelfParty.Select(p => new PartyPachimon(p.PlayerPachimonId, p.PachimonId)).ToArray(),
                payload.OpponentPartyPachimonIds,
                payload.MaxSelectionCount,
                payload.RemainingSeconds,
                payload.SelectionSubmitted);

        private static BattleStartPayload ToDomain(Contracts.BattleStartPayload payload) =>
            new(
                ToDomain(payload.Self),
                ToDomain(payload.Opponent),
                payload.TurnTimeLimitSeconds,
                payload.SelfMoves
                    .Select(set => new PachimonMoveSet(set.Moves
                        .Select(m => new MoveState(m.MoveId, m.CurrentPp, m.MaxPp))
                        .ToArray()))
                    .ToArray(),
                payload.PlayersRequiringForcedSwitch ?? Array.Empty<string>(),
                payload.ActionSubmitted,
                payload.OpponentDisconnected);

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
            BattleCore.EffectivenessResult.DoubleNotVeryEffective => EffectivenessResult.DoubleNotVeryEffective,
            BattleCore.EffectivenessResult.NotVeryEffective => EffectivenessResult.NotVeryEffective,
            BattleCore.EffectivenessResult.Normal => EffectivenessResult.Normal,
            BattleCore.EffectivenessResult.SuperEffective => EffectivenessResult.SuperEffective,
            BattleCore.EffectivenessResult.DoubleSuperEffective => EffectivenessResult.DoubleSuperEffective,
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
