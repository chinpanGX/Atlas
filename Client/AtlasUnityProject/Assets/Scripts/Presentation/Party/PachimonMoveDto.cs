namespace Atlas.Presentation.Party
{
    public sealed class PachimonMoveDto
    {
        // 1始まり(技スロット1〜4)。
        public int Slot;
        public string Name;
        public string TypeName;
        // 対戦外(パーティ編成)では最大値と同じ。
        public int CurrentPp;
        public int MaxPp;
    }
}
