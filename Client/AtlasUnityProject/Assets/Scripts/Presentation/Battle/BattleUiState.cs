using System.Collections.Generic;

namespace Atlas.Presentation.Battle
{
    public sealed class BattleUiState
    {
        public SelfInfoDto SelfInfo;
        public OpponentInfoDto OpponentInfo;
        public List<CommandDto> Commands;
    }

    public record SelfInfoDto
    {
        public string PachimonName;
        public int CurrentHp;
        public int MaxHp;
        public float CurrentHpGauge;
    }

    public record OpponentInfoDto
    {
        public string PachimonName;
        public string CurrentHpPercent;
        public float CurrentHpGauge;
    }

    public record CommandDto
    {
        public int SlotNo;
        public string Name;
    }
}
