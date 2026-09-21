namespace Atlas.BattleCore
{
    // 「技効果後処理Section」が発火するEvent。現時点で反応するEventHandlerは存在しないが、
    // 将来の追加効果(やけど付与等)はこのEventに反応するEventHandlerとして実装し、
    // Section本体(BattleEngine/TurnResolver/DamageCalculator)は変更しない
    // (docs/design/battle.md「内部構造」参照)。
    public sealed record MoveHitEvent(
        BattleSideId Attacker,
        BattleSideId Defender,
        MoveData Move,
        int DamageDealt,
        bool Critical,
        EffectivenessResult Effectiveness);

    public interface IMoveHitEventHandler
    {
        void OnMoveHit(BattleState state, MoveHitEvent evt);
    }
}
