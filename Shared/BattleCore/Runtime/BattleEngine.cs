using System;
using System.Collections.Generic;

namespace Atlas.BattleCore
{
    // Atlas.BattleCoreの唯一の公開エントリポイント。呼び出し側(Client Mock/バトルサーバーの
    // Hub実装)はターン処理Section以下の内部構造(Section/Event/EventHandler)を知らなくてよい
    // (docs/design/battle.md「内部構造(Section / Event / EventHandler)」参照)。
    public static class BattleEngine
    {
        public static TurnResult ProcessTurn(
            BattleState state,
            PlayerAction? player1Action,
            PlayerAction? player2Action,
            ITypeChart typeChart,
            IRandomSource random,
            IReadOnlyList<IMoveHitEventHandler>? moveHitEventHandlers = null)
        {
            int turnNumber = state.AdvanceTurn();
            moveHitEventHandlers ??= Array.Empty<IMoveHitEventHandler>();

            // 強制交代チェックSection: 前ターンで瀕死になった側はSwitch以外の行動を非行動化する。
            PlayerAction? p1Effective = ResolveForcedSwitch(state.Player1, player1Action);
            PlayerAction? p2Effective = ResolveForcedSwitch(state.Player2, player2Action);

            // 行動順決定Section
            var order = TurnResolver.DetermineOrder(state, p1Effective, p2Effective, random);

            var actions = new List<BattleActionOutcome>();
            BattleEndResult? battleEnd = null;

            // 行動実行Section: 行動順に1体ずつ処理。全滅による決着が付いたら即座に打ち切る。
            foreach (var (side, action) in order)
            {
                var outcome = action.Kind == ActionKind.Switch
                    ? ExecuteSwitch(state, side, action)
                    : ExecuteMove(state, side, action, typeChart, random, moveHitEventHandlers);

                actions.Add(outcome.Outcome);

                if (outcome.BattleEnd is { } end)
                {
                    battleEnd = end;
                    break;
                }
            }

            // 実際に処理されなかった側(非行動・強制交代未対応・タイムアウト)をSkipとして報告する。
            AppendSkipIfNeeded(actions, BattleSideId.Player1, p1Effective);
            AppendSkipIfNeeded(actions, BattleSideId.Player2, p2Effective);

            var playersRequiringForcedSwitch = battleEnd is null
                ? CollectPlayersRequiringForcedSwitch(state)
                : Array.Empty<BattleSideId>();

            return new TurnResult(turnNumber, actions, playersRequiringForcedSwitch, battleEnd);
        }

        private static PlayerAction? ResolveForcedSwitch(BattleSide side, PlayerAction? action)
        {
            if (!side.RequiresForcedSwitch)
            {
                return action;
            }

            if (action is { Kind: ActionKind.Switch })
            {
                return action;
            }

            // 強制交代未対応 → 非行動
            return null;
        }

        private static void AppendSkipIfNeeded(List<BattleActionOutcome> actions, BattleSideId side, PlayerAction? effectiveAction)
        {
            if (effectiveAction is not null)
            {
                return;
            }

            actions.Add(new BattleActionOutcome(
                Side: side,
                Kind: BattleActionKind.Skip,
                MoveIndex: null,
                Hit: false,
                Critical: false,
                Effectiveness: EffectivenessResult.Normal,
                DamageDealt: 0,
                TargetRemainingHp: 0,
                TargetFainted: false,
                NewActiveIndex: null));
        }

        private static (BattleActionOutcome Outcome, BattleEndResult? BattleEnd) ExecuteSwitch(
            BattleState state, BattleSideId side, PlayerAction action)
        {
            var battleSide = state.GetSide(side);
            battleSide.SwitchTo(action.PartySlot);

            var outcome = new BattleActionOutcome(
                Side: side,
                Kind: BattleActionKind.Switch,
                MoveIndex: null,
                Hit: true,
                Critical: false,
                Effectiveness: EffectivenessResult.Normal,
                DamageDealt: 0,
                TargetRemainingHp: battleSide.Active.CurrentHp,
                TargetFainted: false,
                NewActiveIndex: action.PartySlot);

            return (outcome, null);
        }

        private static (BattleActionOutcome Outcome, BattleEndResult? BattleEnd) ExecuteMove(
            BattleState state,
            BattleSideId side,
            PlayerAction action,
            ITypeChart typeChart,
            IRandomSource random,
            IReadOnlyList<IMoveHitEventHandler> moveHitEventHandlers)
        {
            var attackerSide = state.GetSide(side);

            // 自身が同ターン中の先行行動で既に瀕死になっている場合は非行動(Skip)扱い。
            if (attackerSide.Active.IsFainted)
            {
                return (SkipOutcome(side), null);
            }

            var defenderSideId = state.GetOpponentSideId(side);
            var defenderSide = state.GetSide(defenderSideId);
            var attacker = attackerSide.Active;
            var defender = defenderSide.Active;
            var move = attacker.Moves[action.MoveIndex];

            // 命中判定Section
            bool hit = TurnResolver.RollHit(move, random);
            if (!hit)
            {
                var missOutcome = new BattleActionOutcome(
                    Side: side,
                    Kind: BattleActionKind.Move,
                    MoveIndex: action.MoveIndex,
                    Hit: false,
                    Critical: false,
                    Effectiveness: EffectivenessResult.Normal,
                    DamageDealt: 0,
                    TargetRemainingHp: defender.CurrentHp,
                    TargetFainted: false,
                    NewActiveIndex: null);
                return (missOutcome, null);
            }

            // 状態技(category=status)は追加効果を実装しない都合上、現状は効果なし。
            if (move.Category == MoveCategory.Status)
            {
                var statusOutcome = new BattleActionOutcome(
                    Side: side,
                    Kind: BattleActionKind.Move,
                    MoveIndex: action.MoveIndex,
                    Hit: true,
                    Critical: false,
                    Effectiveness: EffectivenessResult.Normal,
                    DamageDealt: 0,
                    TargetRemainingHp: defender.CurrentHp,
                    TargetFainted: false,
                    NewActiveIndex: null);
                return (statusOutcome, null);
            }

            // ダメージ付与Section(内部でダメージ計算Section: 攻撃力/防御力決定・ダメージ算出)
            var damageResult = DamageCalculator.Calculate(attacker.Stats, defender.Stats, move, typeChart, random);
            defender.ApplyDamage(damageResult.Damage);

            // 瀕死チェックSection
            bool targetFainted = defender.IsFainted;
            BattleEndResult? battleEnd = null;
            if (targetFainted)
            {
                if (defenderSide.AllFainted)
                {
                    battleEnd = new BattleEndResult(side, BattleEndReason.AllFainted);
                }
                else
                {
                    defenderSide.RequiresForcedSwitch = true;
                }
            }

            // 技効果後処理Section: 技命中後Eventを発火(現状反応するEventHandlerは0個)
            var moveHitEvent = new MoveHitEvent(side, defenderSideId, move, damageResult.Damage, damageResult.Critical, damageResult.Effectiveness);
            foreach (var handler in moveHitEventHandlers)
            {
                handler.OnMoveHit(state, moveHitEvent);
            }

            var outcome = new BattleActionOutcome(
                Side: side,
                Kind: BattleActionKind.Move,
                MoveIndex: action.MoveIndex,
                Hit: true,
                Critical: damageResult.Critical,
                Effectiveness: damageResult.Effectiveness,
                DamageDealt: damageResult.Damage,
                TargetRemainingHp: defender.CurrentHp,
                TargetFainted: targetFainted,
                NewActiveIndex: null);

            return (outcome, battleEnd);
        }

        private static BattleActionOutcome SkipOutcome(BattleSideId side) => new(
            Side: side,
            Kind: BattleActionKind.Skip,
            MoveIndex: null,
            Hit: false,
            Critical: false,
            Effectiveness: EffectivenessResult.Normal,
            DamageDealt: 0,
            TargetRemainingHp: 0,
            TargetFainted: false,
            NewActiveIndex: null);

        private static IReadOnlyList<BattleSideId> CollectPlayersRequiringForcedSwitch(BattleState state)
        {
            var result = new List<BattleSideId>();
            if (state.Player1.RequiresForcedSwitch)
            {
                result.Add(BattleSideId.Player1);
            }

            if (state.Player2.RequiresForcedSwitch)
            {
                result.Add(BattleSideId.Player2);
            }

            return result;
        }
    }
}
