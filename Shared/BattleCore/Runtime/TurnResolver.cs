using System.Collections.Generic;

namespace Atlas.BattleCore
{
    // 行動順決定Section・命中判定Section。
    public static class TurnResolver
    {
        // 交代は必ず技より先。技同士は実効speedが高い方が先、同値ならランダム。
        // 行動が無い側(null)は結果に含まれない。
        public static IReadOnlyList<(BattleSideId Side, PlayerAction Action)> DetermineOrder(
            BattleState state,
            PlayerAction? player1Action,
            PlayerAction? player2Action,
            IRandomSource random)
        {
            var actions = new List<(BattleSideId Side, PlayerAction Action)>();
            if (player1Action is { } p1)
            {
                actions.Add((BattleSideId.Player1, p1));
            }

            if (player2Action is { } p2)
            {
                actions.Add((BattleSideId.Player2, p2));
            }

            if (actions.Count < 2)
            {
                return actions;
            }

            var (sideA, actionA) = actions[0];
            var (sideB, actionB) = actions[1];

            bool aFirst;
            if (actionA.Kind != actionB.Kind)
            {
                // 交代 > 技
                aFirst = actionA.Kind == ActionKind.Switch;
            }
            else if (actionA.Kind == ActionKind.Switch)
            {
                // 両者交代: どちらが先でも盤面に影響しないためP1を先とする
                aFirst = true;
            }
            else
            {
                int speedA = state.GetSide(sideA).Active.Stats.Speed;
                int speedB = state.GetSide(sideB).Active.Stats.Speed;
                aFirst = speedA == speedB ? random.NextInt(0, 1) == 0 : speedA > speedB;
            }

            return aFirst
                ? actions
                : new List<(BattleSideId Side, PlayerAction Action)> { actions[1], actions[0] };
        }

        // 乱数(1〜100) <= move.accuracy なら命中。
        public static bool RollHit(MoveData move, IRandomSource random) => random.NextInt(1, 100) <= move.Accuracy;
    }
}
