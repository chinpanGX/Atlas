using System.Collections.Concurrent;
using Atlas.BattleCore;
using Atlas.BattleServer.Auth;
using Atlas.BattleServer.Contracts;
using Atlas.BattleServer.Internal;
using Cysharp.Runtime.Multicast;
using Grpc.Core;
using MagicOnion;
using MagicOnion.Server.Hubs;

namespace Atlas.BattleServer.Battle
{
    public sealed record JoinOutcome(JoinResultStatus Status, BattleSession? Session, int Slot);

    // サーバー全体の対戦状態(ConcurrentDictionary<matchId, BattleSession>)と、その進行ロジックを持つ。
    // 猶予タイマー・ターンタイマーはHubインスタンス(1接続=1インスタンス)の外で発火するため、
    // 進行ロジックはHubではなくこのシングルトンに置き、Hubは呼び出しを委譲するだけにする。
    public sealed class BattleCoordinator(
        IParticipantDataSource dataSource,
        BattleResultReporter reporter,
        ILogger<BattleCoordinator> logger)
    {
        // docs/design/battle.md「ターンタイムアウト」「切断・再接続」の仮値。
        public const int TurnTimeLimitSeconds = 30;
        private static readonly TimeSpan ReconnectGracePeriod = TimeSpan.FromSeconds(60);

        // 決着後もしばらく残し、遅れて来たJoinAsyncにMatchNotFoundを返せるようにする
        // (battle_tokenの有効期限30秒+ClockSkewより長ければよい)。
        private static readonly TimeSpan FinishedSessionRetention = TimeSpan.FromSeconds(60);

        private const int MaxSelectionCount = 3;

        private readonly ConcurrentDictionary<string, BattleSession> _sessions = new();

        // System.Randomのインスタンスはスレッドセーフでないため、スレッドセーフなRandom.Sharedを使う。
        private readonly IRandomSource _random = new SystemRandomSource(Random.Shared);

        public async Task<JoinOutcome> JoinAsync(
            BattleTokenValidationResult token,
            string matchId,
            Guid connectionId,
            Func<string, ValueTask<IGroup<IBattleHubReceiver>>> addToGroup)
        {
            if (token.Claims is not { } claims || token.Status == BattleTokenStatus.Invalid || claims.MatchId != matchId)
            {
                return new JoinOutcome(JoinResultStatus.InvalidToken, null, -1);
            }

            BattleSession? session;
            if (token.Status == BattleTokenStatus.Valid)
            {
                session = _sessions.GetOrAdd(matchId, id => new BattleSession(id));
            }
            else if (!_sessions.TryGetValue(matchId, out session))
            {
                // 期限切れトークンは既存の対戦への再接続にのみ使える。
                return new JoinOutcome(JoinResultStatus.InvalidToken, null, -1);
            }

            await session.Gate.WaitAsync();
            try
            {
                if (session.Phase == BattlePhase.Finished)
                {
                    return new JoinOutcome(JoinResultStatus.MatchNotFound, null, -1);
                }

                int slot = session.IndexOf(claims.PlayerId);
                bool isReconnect = slot >= 0;
                if (isReconnect)
                {
                    if (session.Participants[slot]!.ConnectionId is not null)
                    {
                        return new JoinOutcome(JoinResultStatus.AlreadyJoined, null, -1);
                    }
                }
                else
                {
                    // battle_tokenの有効期限(30秒)は再接続猶予(60秒)より短いため、再接続時に限り
                    // 期限切れ(署名は正当)のトークンを許容する。新規参加には有効なトークンを要求する。
                    if (token.Status == BattleTokenStatus.Expired)
                    {
                        return new JoinOutcome(JoinResultStatus.InvalidToken, null, -1);
                    }

                    slot = Array.IndexOf(session.Participants, null);
                    if (slot < 0)
                    {
                        // Rust側は1対戦につき2人分しかトークンを発行しないため、通常は起こらない。
                        logger.LogWarning("Third player tried to join. matchId={MatchId} playerId={PlayerId}", matchId, claims.PlayerId);
                        return new JoinOutcome(JoinResultStatus.InvalidToken, null, -1);
                    }

                    session.Participants[slot] = new BattleParticipant(claims.PlayerId);
                }

                var participant = session.Participants[slot]!;
                participant.ConnectionId = connectionId;
                session.Group = await addToGroup(matchId);
                CancelTimer(session.AbsenceTimers[slot]);
                session.AbsenceTimers[slot] = null;

                if (isReconnect)
                {
                    logger.LogInformation("Player reconnected. matchId={MatchId} playerId={PlayerId}", matchId, claims.PlayerId);
                    session.Group.Except(connectionId).OnOpponentReconnected();

                    // 専用の再同期メソッドは設けず、現在の盤面をOnMatchStartとして再接続者にだけ再送する。
                    if (session.Phase == BattlePhase.InProgress)
                    {
                        session.Group.Single(connectionId).OnMatchStart(BuildStartPayload(session, slot));
                        if (session.AllConnected)
                        {
                            StartTurnTimer(session);
                        }
                    }
                }
                else
                {
                    logger.LogInformation("Player joined. matchId={MatchId} playerId={PlayerId} slot={Slot}", matchId, claims.PlayerId, slot);
                    int otherSlot = 1 - slot;
                    if (session.Participants[otherSlot] is null)
                    {
                        // 相手が来ない場合も切断と同じ猶予で打ち切る。
                        StartAbsenceTimer(session, otherSlot);
                    }
                    else if (session.Phase == BattlePhase.WaitingForJoin)
                    {
                        session.Phase = BattlePhase.Selecting;
                    }
                }

                return new JoinOutcome(JoinResultStatus.Success, session, slot);
            }
            finally
            {
                session.Gate.Release();
            }
        }

        public async Task SubmitSelectionAsync(BattleSession session, int slot, string[] playerPachimonIds)
        {
            await session.Gate.WaitAsync();
            try
            {
                var participant = session.Participants[slot]!;
                if (participant.SelectedIds is not null)
                {
                    // 再接続後にクライアントが自動送信し直すケースを想定し、2回目以降は無視する。
                    return;
                }

                if (session.Phase is not (BattlePhase.WaitingForJoin or BattlePhase.Selecting))
                {
                    throw new ReturnStatusException(StatusCode.FailedPrecondition, "selection is not accepted in this phase");
                }

                if (playerPachimonIds.Length is < 1 or > MaxSelectionCount
                    || playerPachimonIds.Any(string.IsNullOrEmpty)
                    || playerPachimonIds.Distinct().Count() != playerPachimonIds.Length)
                {
                    throw new ReturnStatusException(StatusCode.InvalidArgument, "invalid selection");
                }

                var loadouts = new ParticipantLoadout[playerPachimonIds.Length];
                for (int i = 0; i < playerPachimonIds.Length; i++)
                {
                    loadouts[i] = dataSource.Resolve(participant.PlayerId, playerPachimonIds[i])
                        ?? throw new ReturnStatusException(StatusCode.InvalidArgument, $"unknown player_pachimon_id: {playerPachimonIds[i]}");
                }

                participant.SelectedIds = playerPachimonIds;
                participant.Loadouts = loadouts;
                participant.Revealed = new bool[loadouts.Length];

                if (session.Participants.All(p => p?.SelectedIds is not null))
                {
                    StartBattle(session);
                }
            }
            finally
            {
                session.Gate.Release();
            }
        }

        public Task SubmitMoveAsync(BattleSession session, int slot, MoveRequest move) =>
            SubmitActionAsync(session, slot, side =>
            {
                if (side.RequiresForcedSwitch)
                {
                    throw new ReturnStatusException(StatusCode.FailedPrecondition, "forced switch is required");
                }

                var moves = session.Participants[slot]!.Loadouts![side.ActiveIndex].Moves;
                int moveIndex = moves.ToList().FindIndex(m => m.MoveId == move.MoveId);
                if (moveIndex < 0)
                {
                    throw new ReturnStatusException(StatusCode.InvalidArgument, $"unknown move: {move.MoveId}");
                }

                // BattleCoreはPP0の技を渡されると例外を投げるため、ここで弾く。
                if (side.Active.CurrentPp[moveIndex] <= 0)
                {
                    throw new ReturnStatusException(StatusCode.FailedPrecondition, $"no PP left: {move.MoveId}");
                }

                return new PendingAction(PlayerAction.UseMove(moveIndex), move.MoveId);
            });

        public Task SwitchAsync(BattleSession session, int slot, int partySlot) =>
            SubmitActionAsync(session, slot, side =>
            {
                if (partySlot < 0 || partySlot >= side.Party.Count
                    || side.Party[partySlot].IsFainted
                    || partySlot == side.ActiveIndex)
                {
                    throw new ReturnStatusException(StatusCode.InvalidArgument, $"cannot switch to slot {partySlot}");
                }

                return new PendingAction(PlayerAction.SwitchTo(partySlot), null);
            });

        public async Task ForfeitAsync(BattleSession session, int slot)
        {
            await session.Gate.WaitAsync();
            try
            {
                if (session.Phase == BattlePhase.Finished)
                {
                    return;
                }

                if (session.Participants[1 - slot] is null)
                {
                    throw new ReturnStatusException(StatusCode.FailedPrecondition, "opponent has not joined yet");
                }

                Finish(session, 1 - slot, BattleEndReason.Forfeit);
            }
            finally
            {
                session.Gate.Release();
            }
        }

        public async Task HandleDisconnectAsync(BattleSession session, int slot, Guid connectionId)
        {
            await session.Gate.WaitAsync();
            try
            {
                var participant = session.Participants[slot]!;
                if (session.Phase == BattlePhase.Finished || participant.ConnectionId != connectionId)
                {
                    return;
                }

                logger.LogInformation("Player disconnected. matchId={MatchId} playerId={PlayerId}", session.MatchId, participant.PlayerId);
                participant.ConnectionId = null;
                session.Group?.Except(connectionId).OnOpponentDisconnected();

                // 猶予中は両者とも行動を受け付けない(ターンを進めない)。
                CancelTimer(session.TurnTimer);
                session.TurnTimer = null;
                StartAbsenceTimer(session, slot);
            }
            finally
            {
                session.Gate.Release();
            }
        }

        private async Task SubmitActionAsync(BattleSession session, int slot, Func<BattleSide, PendingAction> createAction)
        {
            await session.Gate.WaitAsync();
            try
            {
                if (session.Phase != BattlePhase.InProgress)
                {
                    throw new ReturnStatusException(StatusCode.FailedPrecondition, "battle is not in progress");
                }

                if (!session.AllConnected)
                {
                    throw new ReturnStatusException(StatusCode.FailedPrecondition, "waiting for opponent to reconnect");
                }

                var participant = session.Participants[slot]!;
                if (participant.Pending is not null)
                {
                    throw new ReturnStatusException(StatusCode.FailedPrecondition, "action already submitted for this turn");
                }

                participant.Pending = createAction(session.Core!.GetSide(ToSideId(slot)));

                if (session.Participants.All(p => p!.Pending is not null))
                {
                    ResolveTurn(session);
                }
            }
            finally
            {
                session.Gate.Release();
            }
        }

        private void StartBattle(BattleSession session)
        {
            var sides = session.Participants.Select(p => new BattleSide(
                p!.Loadouts!.Select(l => new PachimonState(l.Stats, l.Moves.Select(m => m.Data).ToList())).ToList())).ToArray();
            session.Core = new BattleState(sides[0], sides[1]);
            session.Phase = BattlePhase.InProgress;

            foreach (var participant in session.Participants)
            {
                participant!.Revealed[0] = true;
            }

            logger.LogInformation("Battle started. matchId={MatchId}", session.MatchId);

            // Self/Opponentが受信者ごとに異なるため、全体ブロードキャストではなく個別に送る。
            for (int slot = 0; slot < session.Participants.Length; slot++)
            {
                if (session.Participants[slot]!.ConnectionId is { } connectionId)
                {
                    session.Group!.Single(connectionId).OnMatchStart(BuildStartPayload(session, slot));
                }
            }

            if (session.AllConnected)
            {
                StartTurnTimer(session);
            }
        }

        // 両者の行動が揃った、またはターンタイムアウトした時点で呼ぶ。未提出の側は非行動(null)として扱う。
        private void ResolveTurn(BattleSession session)
        {
            CancelTimer(session.TurnTimer);
            session.TurnTimer = null;

            var pending = session.Participants.Select(p => p!.Pending).ToArray();
            foreach (var participant in session.Participants)
            {
                participant!.Pending = null;
            }

            var core = session.Core!;
            var result = BattleEngine.ProcessTurn(core, pending[0]?.Action, pending[1]?.Action, dataSource.TypeChart, _random);

            var actions = new List<ActionResult>(result.Actions.Count);
            foreach (var outcome in result.Actions)
            {
                int actorSlot = ToSlot(outcome.Side);
                actions.Add(ToActionResult(session, outcome, pending[actorSlot]?.MoveId));
                session.TurnLog.Add(ToTurnRecord(session, result.TurnNumber, outcome, pending[actorSlot]?.MoveId));
            }

            var payload = new TurnResultPayload(
                result.TurnNumber,
                actions.ToArray(),
                result.PlayersRequiringForcedSwitch.Select(s => session.Participants[ToSlot(s)]!.PlayerId).ToArray());
            session.Group?.All.OnTurnResult(payload);

            if (result.BattleEnd is { } end)
            {
                Finish(session, ToSlot(end.Winner), end.Reason);
            }
            else
            {
                StartTurnTimer(session);
            }
        }

        private ActionResult ToActionResult(BattleSession session, BattleActionOutcome outcome, string? moveId)
        {
            int actorSlot = ToSlot(outcome.Side);
            var actor = session.Participants[actorSlot]!;
            string playerId = actor.PlayerId;
            var core = session.Core!;

            switch (outcome.Kind)
            {
                case BattleActionKind.Move:
                {
                    // 交代は必ず技より先に処理されるため、ターン終了後の相手の場のパチモンが技の対象そのもの。
                    var target = core.GetSide(core.GetOpponentSideId(outcome.Side)).Active;
                    return new ActionResult(
                        playerId, ActionType.Move, moveId, outcome.Hit, outcome.Critical, outcome.Effectiveness,
                        HpPercent(outcome.TargetRemainingHp, target.Stats.Hp), outcome.TargetFainted,
                        NewActiveIndex: null, RevealedPachimon: null);
                }
                case BattleActionKind.Switch:
                {
                    int newIndex = outcome.NewActiveIndex!.Value;
                    var switchedIn = core.GetSide(outcome.Side).Party[newIndex];
                    PachimonBattleState? revealed = null;
                    if (!actor.Revealed[newIndex])
                    {
                        actor.Revealed[newIndex] = true;
                        revealed = ToPachimonBattleState(actor, core.GetSide(outcome.Side), newIndex);
                    }

                    return new ActionResult(
                        playerId, ActionType.Switch, null, Hit: true, Critical: false, EffectivenessResult.Normal,
                        HpPercent(outcome.TargetRemainingHp, switchedIn.Stats.Hp), TargetFainted: false,
                        newIndex, revealed);
                }
                default:
                    return new ActionResult(
                        playerId, ActionType.Skip, null, Hit: false, Critical: false, EffectivenessResult.Normal,
                        TargetRemainingHpPercent: 0, TargetFainted: false, NewActiveIndex: null, RevealedPachimon: null);
            }
        }

        private static BattleTurnRecord ToTurnRecord(BattleSession session, int turnNumber, BattleActionOutcome outcome, string? moveId)
        {
            var actionData = new TurnActionData(
                outcome.Kind.ToString(),
                outcome.Kind == BattleActionKind.Move ? moveId : null,
                outcome.Kind == BattleActionKind.Switch ? outcome.NewActiveIndex : null);
            var resultData = new TurnResultData(
                outcome.Hit, outcome.Critical, outcome.Effectiveness.ToString(), outcome.DamageDealt,
                outcome.TargetRemainingHp, outcome.TargetFainted, outcome.NewActiveIndex);
            return new BattleTurnRecord(turnNumber, session.Participants[ToSlot(outcome.Side)]!.PlayerId, actionData, resultData);
        }

        private void Finish(BattleSession session, int winnerSlot, BattleEndReason reason)
        {
            session.Phase = BattlePhase.Finished;
            CancelTimer(session.TurnTimer);
            session.TurnTimer = null;
            for (int i = 0; i < session.AbsenceTimers.Length; i++)
            {
                CancelTimer(session.AbsenceTimers[i]);
                session.AbsenceTimers[i] = null;
            }

            string winnerId = session.Participants[winnerSlot]!.PlayerId;
            logger.LogInformation("Battle finished. matchId={MatchId} winnerId={WinnerId} reason={Reason}", session.MatchId, winnerId, reason);
            session.Group?.All.OnBattleEnd(new BattleEndPayload(winnerId, reason));

            var request = new BattleResultRequest(
                session.MatchId,
                winnerId,
                session.Participants[0]?.SelectedIds ?? [],
                session.Participants[1]?.SelectedIds ?? [],
                session.TurnLog.ToList());

            // ReportAsyncは内部で例外を握りつぶしてログに残すため、完了を待たずにGateを解放してよい。
            _ = reporter.ReportAsync(request);
            ScheduleRemoval(session);
        }

        private void StartTurnTimer(BattleSession session)
        {
            CancelTimer(session.TurnTimer);
            var cts = new CancellationTokenSource();
            session.TurnTimer = cts;
            _ = RunTimerAsync(TimeSpan.FromSeconds(TurnTimeLimitSeconds), cts.Token, async () =>
            {
                await session.Gate.WaitAsync();
                try
                {
                    // Gate待ちの間に行動が揃った・切断された等でキャンセル済みなら何もしない。
                    if (cts.IsCancellationRequested || session.Phase != BattlePhase.InProgress)
                    {
                        return;
                    }

                    logger.LogInformation("Turn timed out. matchId={MatchId}", session.MatchId);
                    ResolveTurn(session);
                }
                finally
                {
                    session.Gate.Release();
                }
            });
        }

        private void StartAbsenceTimer(BattleSession session, int slot)
        {
            CancelTimer(session.AbsenceTimers[slot]);
            var cts = new CancellationTokenSource();
            session.AbsenceTimers[slot] = cts;

            _ = RunTimerAsync(ReconnectGracePeriod, cts.Token, async () =>
            {
                await session.Gate.WaitAsync();
                try
                {
                    if (cts.IsCancellationRequested || session.Phase == BattlePhase.Finished)
                    {
                        return;
                    }

                    var absent = session.Participants[slot];
                    if (absent?.ConnectionId is not null)
                    {
                        return;
                    }

                    var other = session.Participants[1 - slot];
                    if (other is null || (session.Phase != BattlePhase.InProgress && other.ConnectionId is null))
                    {
                        // 対戦開始前に両者とも不在になった: 勝者を決められないため結果報告せず破棄する。
                        logger.LogInformation("Battle discarded before start. matchId={MatchId}", session.MatchId);
                        session.Phase = BattlePhase.Finished;
                        ScheduleRemoval(session);
                        return;
                    }

                    Finish(session, 1 - slot, BattleEndReason.DisconnectTimeout);
                }
                finally
                {
                    session.Gate.Release();
                }
            });
        }

        private void ScheduleRemoval(BattleSession session)
        {
            _ = RunTimerAsync(FinishedSessionRetention, CancellationToken.None, () =>
            {
                _sessions.TryRemove(new KeyValuePair<string, BattleSession>(session.MatchId, session));
                return Task.CompletedTask;
            });
        }

        private async Task RunTimerAsync(TimeSpan delay, CancellationToken cancellationToken, Func<Task> onElapsed)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await onElapsed();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Battle timer callback failed");
            }
        }

        private static void CancelTimer(CancellationTokenSource? timer) => timer?.Cancel();

        private static BattleStartPayload BuildStartPayload(BattleSession session, int selfSlot)
        {
            var core = session.Core!;
            return new BattleStartPayload(
                BuildSnapshot(session.Participants[selfSlot]!, core.GetSide(ToSideId(selfSlot)), revealAll: true),
                BuildSnapshot(session.Participants[1 - selfSlot]!, core.GetSide(ToSideId(1 - selfSlot)), revealAll: false),
                TurnTimeLimitSeconds);
        }

        private static ParticipantSnapshot BuildSnapshot(BattleParticipant participant, BattleSide side, bool revealAll)
        {
            var slots = new PachimonSlot[side.Party.Count];
            for (int i = 0; i < slots.Length; i++)
            {
                bool revealed = revealAll || participant.Revealed[i];
                slots[i] = new PachimonSlot(revealed, revealed ? ToPachimonBattleState(participant, side, i) : null);
            }

            return new ParticipantSnapshot(participant.PlayerId, slots, side.ActiveIndex);
        }

        private static PachimonBattleState ToPachimonBattleState(BattleParticipant participant, BattleSide side, int index)
        {
            var state = side.Party[index];
            return new PachimonBattleState(
                participant.SelectedIds![index],
                participant.Loadouts![index].PachimonId,
                HpPercent(state.CurrentHp, state.Stats.Hp),
                state.IsFainted);
        }

        // 0〜100の整数に丸める。生存しているのに0%と表示されないよう、HPが残っていれば最低1にする。
        private static int HpPercent(int currentHp, int maxHp)
        {
            if (currentHp <= 0)
            {
                return 0;
            }

            return Math.Clamp((int)Math.Round(currentHp * 100.0 / maxHp), 1, 100);
        }

        private static BattleSideId ToSideId(int slot) => slot == 0 ? BattleSideId.Player1 : BattleSideId.Player2;

        private static int ToSlot(BattleSideId side) => side == BattleSideId.Player1 ? 0 : 1;
    }
}
