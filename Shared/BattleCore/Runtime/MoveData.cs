namespace Atlas.BattleCore
{
    public sealed record MoveData(
        ElementType MoveType,
        MoveCategory Category,
        int BasePower,
        int Accuracy,
        int MaxPp);
}
