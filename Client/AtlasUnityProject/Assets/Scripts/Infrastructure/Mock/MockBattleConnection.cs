using System;
using System.Collections.Generic;
using Atlas.BattleCore;
using Atlas.Domain;
using Cysharp.Threading.Tasks;
using MasterMemory;
using ZLinq;

namespace Atlas.Infrastructure.Mock
{
    // design/battle.md「Stage 1(クライアント単体)」。ネットワークを介さず、Atlas.BattleCoreを
    // プロセス内でそのまま呼び出して両プレイヤー分の行動を解決する。対戦相手は簡易AIで代替する。
    // player_pachimon(プレイヤー所持データ)がまだ無いため、選出パーティはTestPartyFactoryで
    // マスターデータから直接組み立てる。
    public sealed class MockBattleConnection : IBattleConnection
    {
        private const int TurnTimeLimitSeconds = 30;
        private const string SelfPlayerId = "mock-self";
        private const string OpponentPlayerId = "mock-opponent";

        // 対戦相手の選出3体は固定(design/battle.md「battleToken/matchIdもこの段階ではダミー値で
        // 構わない」と同じ考え方で、Stage 1では相手パーティも固定で構わない)。
        private static readonly int[] OpponentPachimonIds = { 1004, 1005, 1006 };

        private readonly MemoryDatabase database;
        private readonly IRandomSource random;
        private readonly ITypeChart typeChart;

        private BattleState state;
        private IReadOnlyList<PartyMember> selfMembers;
        private IReadOnlyList<PartyMember> opponentMembers;
        private bool[] opponentRevealed;

        public event Action<BattleStartPayload> OnMatchStart;
        public event Action<TurnResultPayload> OnTurnResult;
        public event Action<BattleEndPayload> OnBattleEnd;
        public event Action OnOpponentDisconnected;
        public event Action OnOpponentReconnected;

        public MockBattleConnection(MemoryDatabase database, IRandomSource random = null)
        {
            this.database = database;
            this.random = random ?? new SystemRandomSource();
            typeChart = new MasterDataTypeChart(database);
        }

        public UniTask<JoinResult> JoinAsync(string battleToken, string matchId)
        {
            return UniTask.FromResult(new JoinResult(JoinResultStatus.Success));
        }

        public UniTask SubmitSelectionAsync(string[] playerPachimonIds)
        {
            selfMembers = TestPartyFactory.BuildParty(database, playerPachimonIds.Select(int.Parse).ToArray());
            opponentMembers = TestPartyFactory.BuildParty(database, OpponentPachimonIds);

            state = new BattleState(
                new BattleSide(selfMembers.Select(m => m.State).ToList()),
                new BattleSide(opponentMembers.Select(m => m.State).ToList()));

            // design/battle.md「PachimonSlot」: 自分は常に全枠公開、相手は最初に場に出た1体だけ公開。
            opponentRevealed = new bool[opponentMembers.Count];
            opponentRevealed[state.Player2.ActiveIndex] = true;
            var selfRevealed = selfMembers.Select(_ => true).ToArray();

            OnMatchStart?.Invoke(new BattleStartPayload(
                BuildSnapshot(SelfPlayerId, state.Player1, selfMembers, selfRevealed),
                BuildSnapshot(OpponentPlayerId, state.Player2, opponentMembers, opponentRevealed),
                TurnTimeLimitSeconds,
                BuildSelfMoves(selfMembers)));

            return UniTask.CompletedTask;
        }

        public UniTask SubmitMoveAsync(MoveRequest move)
        {
            var activeMoveIds = selfMembers[state.Player1.ActiveIndex].MoveIds;
            var moveIndex = IndexOfMoveId(activeMoveIds, move.MoveId);
            return ProcessTurnAsync(PlayerAction.UseMove(moveIndex));
        }

        public UniTask SwitchAsync(int partySlot)
        {
            return ProcessTurnAsync(PlayerAction.SwitchTo(partySlot));
        }

        public UniTask ForfeitAsync()
        {
            OnBattleEnd?.Invoke(new BattleEndPayload(OpponentPlayerId, Atlas.Domain.BattleEndReason.Forfeit));
            return UniTask.CompletedTask;
        }

        // 強制交代ターンはBattleServerと同じく、倒れた側の交代だけを処理する(相手は行動しない)。
        // プレイヤーの強制交代ターンでは簡易AIは行動せず、簡易AIの強制交代はプレイヤーの行動を待たずに続けて処理する。
        private UniTask ProcessTurnAsync(PlayerAction selfAction)
        {
            PlayerAction? opponentAction = state.Player1.RequiresForcedSwitch ? null : ChooseOpponentAction();
            if (ResolveTurn(selfAction, opponentAction) && state.Player2.RequiresForcedSwitch)
            {
                ResolveTurn(null, ChooseOpponentAction());
            }

            return UniTask.CompletedTask;
        }

        // 決着が付いていなければtrue。
        private bool ResolveTurn(PlayerAction? selfAction, PlayerAction? opponentAction)
        {
            var turnResult = BattleEngine.ProcessTurn(state, selfAction, opponentAction, typeChart, random);

            OnTurnResult?.Invoke(ToPayload(turnResult));

            if (turnResult.BattleEnd is { } end)
            {
                var winnerId = end.Winner == BattleSideId.Player1 ? SelfPlayerId : OpponentPlayerId;
                OnBattleEnd?.Invoke(new BattleEndPayload(winnerId, ToDomainReason(end.Reason)));
                return false;
            }

            return true;
        }

        // design/battle.md「Stage 1」の簡易AI: 使用可能な技からランダムに1つ選ぶ。強さ・タイプ
        // 相性は考慮しない。自発的な交代はせず、強制交代時のみ選出3体のうち生存している先頭の
        // パチモンに交代する。
        private PlayerAction ChooseOpponentAction()
        {
            var side = state.Player2;
            if (side.RequiresForcedSwitch)
            {
                for (var i = 0; i < side.Party.Count; i++)
                {
                    if (!side.Party[i].IsFainted)
                    {
                        return PlayerAction.SwitchTo(i);
                    }
                }

                throw new InvalidOperationException("生存しているパチモンがいない状態で強制交代が要求されました。");
            }

            var usableMoveIndexes = ValueEnumerable.Range(0, side.Active.Moves.Count)
                .Where(i => side.Active.CurrentPp[i] > 0)
                .ToList();
            var chosen = usableMoveIndexes[random.NextInt(0, usableMoveIndexes.Count - 1)];
            return PlayerAction.UseMove(chosen);
        }

        private TurnResultPayload ToPayload(TurnResult turnResult)
        {
            var actions = turnResult.Actions.Select(ToActionResult).ToArray();
            var forcedSwitchIds = turnResult.PlayersRequiringForcedSwitch
                .Select(side => side == BattleSideId.Player1 ? SelfPlayerId : OpponentPlayerId)
                .ToArray();

            return new TurnResultPayload(turnResult.TurnNumber, actions, forcedSwitchIds);
        }

        private ActionResult ToActionResult(BattleActionOutcome outcome)
        {
            var isPlayer1 = outcome.Side == BattleSideId.Player1;
            var playerId = isPlayer1 ? SelfPlayerId : OpponentPlayerId;
            var actingMembers = isPlayer1 ? selfMembers : opponentMembers;
            var activeIndex = (isPlayer1 ? state.Player1 : state.Player2).ActiveIndex;

            string moveId = null;
            if (outcome.Kind == BattleActionKind.Move && outcome.MoveIndex is { } moveIndex)
            {
                moveId = actingMembers[activeIndex].MoveIds[moveIndex];
            }

            PachimonBattleState revealedPachimon = null;
            if (!isPlayer1 && outcome.Kind == BattleActionKind.Switch && outcome.NewActiveIndex is { } newIndex
                && !opponentRevealed[newIndex])
            {
                opponentRevealed[newIndex] = true;
                revealedPachimon = ToBattleState(opponentMembers[newIndex]);
            }

            return new ActionResult(
                playerId,
                ToDomainActionType(outcome.Kind),
                moveId,
                outcome.Hit,
                outcome.Critical,
                ToDomainEffectiveness(outcome.Effectiveness),
                ResolveTargetHpPercent(outcome),
                outcome.TargetFainted,
                outcome.NewActiveIndex,
                revealedPachimon);
        }

        // Move: targetは行動側の相手側アクティブ個体。Switch: targetは行動側自身の交代先個体
        // (BattleEngine.ExecuteSwitchのTargetRemainingHpと同じ意味、design/battle.md参照)。
        private int ResolveTargetHpPercent(BattleActionOutcome outcome)
        {
            if (outcome.Kind == BattleActionKind.Skip)
            {
                return 0;
            }

            if (outcome.Kind == BattleActionKind.Switch)
            {
                var members = outcome.Side == BattleSideId.Player1 ? selfMembers : opponentMembers;
                return PercentOf(outcome.TargetRemainingHp, members[outcome.NewActiveIndex!.Value].State.Stats.Hp);
            }

            var defenderSideId = outcome.Side == BattleSideId.Player1 ? BattleSideId.Player2 : BattleSideId.Player1;
            var defenderMembers = defenderSideId == BattleSideId.Player1 ? selfMembers : opponentMembers;
            var defenderActiveIndex = state.GetSide(defenderSideId).ActiveIndex;
            return PercentOf(outcome.TargetRemainingHp, defenderMembers[defenderActiveIndex].State.Stats.Hp);
        }

        private static PachimonMoveSet[] BuildSelfMoves(IReadOnlyList<PartyMember> members)
        {
            return members
                .Select(member => new PachimonMoveSet(member.MoveIds
                    .Select((moveId, i) => new MoveState(moveId, member.State.CurrentPp[i], member.State.Moves[i].MaxPp))
                    .ToArray()))
                .ToArray();
        }

        private static ParticipantSnapshot BuildSnapshot(
            string playerId, BattleSide side, IReadOnlyList<PartyMember> members, IReadOnlyList<bool> revealed)
        {
            var slots = new PachimonSlot[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                slots[i] = revealed[i] ? new PachimonSlot(true, ToBattleState(members[i])) : new PachimonSlot(false, null);
            }

            return new ParticipantSnapshot(playerId, slots, side.ActiveIndex);
        }

        private static PachimonBattleState ToBattleState(PartyMember member) => new(
            member.PachimonId.ToString(),
            member.PachimonId,
            PercentOf(member.State.CurrentHp, member.State.Stats.Hp),
            member.State.IsFainted);

        private static int PercentOf(int current, int max) => max == 0 ? 0 : (int)Math.Round(100.0 * current / max);

        private static int IndexOfMoveId(IReadOnlyList<string> moveIds, string moveId)
        {
            for (var i = 0; i < moveIds.Count; i++)
            {
                if (moveIds[i] == moveId)
                {
                    return i;
                }
            }

            throw new ArgumentException($"技ID '{moveId}' は現在選出中のパチモンの技に含まれていません。", nameof(moveId));
        }

        private static ActionType ToDomainActionType(BattleActionKind kind) => kind switch
        {
            BattleActionKind.Move => ActionType.Move,
            BattleActionKind.Switch => ActionType.Switch,
            BattleActionKind.Skip => ActionType.Skip,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        private static Atlas.Domain.EffectivenessResult ToDomainEffectiveness(Atlas.BattleCore.EffectivenessResult effectiveness) => effectiveness switch
        {
            Atlas.BattleCore.EffectivenessResult.Immune => Atlas.Domain.EffectivenessResult.Immune,
            Atlas.BattleCore.EffectivenessResult.NotVeryEffective => Atlas.Domain.EffectivenessResult.NotVeryEffective,
            Atlas.BattleCore.EffectivenessResult.Normal => Atlas.Domain.EffectivenessResult.Normal,
            Atlas.BattleCore.EffectivenessResult.SuperEffective => Atlas.Domain.EffectivenessResult.SuperEffective,
            _ => throw new ArgumentOutOfRangeException(nameof(effectiveness), effectiveness, null),
        };

        private static Atlas.Domain.BattleEndReason ToDomainReason(Atlas.BattleCore.BattleEndReason reason) => reason switch
        {
            Atlas.BattleCore.BattleEndReason.AllFainted => Atlas.Domain.BattleEndReason.AllFainted,
            Atlas.BattleCore.BattleEndReason.Forfeit => Atlas.Domain.BattleEndReason.Forfeit,
            Atlas.BattleCore.BattleEndReason.DisconnectTimeout => Atlas.Domain.BattleEndReason.DisconnectTimeout,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }
}
