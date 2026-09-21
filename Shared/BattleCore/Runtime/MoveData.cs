namespace Atlas.BattleCore
{
    public readonly record struct MoveData(
        ElementType MoveType,
        MoveCategory Category,
        int BasePower,
        int Accuracy);
}
