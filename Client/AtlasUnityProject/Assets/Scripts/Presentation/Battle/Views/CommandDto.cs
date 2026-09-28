namespace Atlas.Presentation.Battle
{
    public record CommandDto
    {
        public int SlotNo;
        public string Name;
        public string TypeName;
        public int CurrentPp;
        public int MaxPp;
        // 場の相手への相性(「バツグン」等)。等倍・変化技・相手が分からないときは空文字。
        public string MatchupText;
    }
}
