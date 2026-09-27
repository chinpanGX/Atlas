namespace Atlas.Presentation.Battle
{
    // Push時に渡す、BattleServerへの参加(JoinAsync)に必要な値。
    public sealed class BattleViewDto
    {
        public string MatchId;
        public string BattleToken;
    }
}
