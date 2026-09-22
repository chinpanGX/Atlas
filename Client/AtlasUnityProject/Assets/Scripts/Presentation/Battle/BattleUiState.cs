using System.Collections.Generic;

namespace Atlas.Presentation.Battle
{
    public sealed class BattleUiState
    {
        public string SelfName;
        public int SelfHpPercent;
        public string OpponentName;
        public int OpponentHpPercent;
        public IReadOnlyList<string> SelfMoveNames;
    }
}
