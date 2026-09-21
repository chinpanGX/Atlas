using System.Collections.Generic;

namespace Atlas.BattleCore
{
    public enum BattleActionKind
    {
        Move,
        Switch,

        // タイムアウト or 強制交代未対応 or 自身が同ターン中に瀕死になったことによる非行動
        Skip,
    }

    public enum BattleEndReason
    {
        AllFainted,
        Forfeit,
        DisconnectTimeout,
    }

    public sealed record BattleActionOutcome(
        BattleSideId Side,
        BattleActionKind Kind,
        int? MoveIndex,
        bool Hit,
        bool Critical,
        EffectivenessResult Effectiveness,
        int DamageDealt,
        int TargetRemainingHp,
        bool TargetFainted,
        int? NewActiveIndex);

    public sealed record BattleEndResult(BattleSideId Winner, BattleEndReason Reason);

    public sealed record TurnResult(
        int TurnNumber,
        IReadOnlyList<BattleActionOutcome> Actions,
        IReadOnlyList<BattleSideId> PlayersRequiringForcedSwitch,
        BattleEndResult? BattleEnd);
}
