using System.Collections.Generic;

namespace Atlas.Presentation.Battle
{
    public sealed record BattleUIStateDto
    {
        public SelfInfoDto SelfInfo;
        public OpponentInfoDto OpponentInfo;
        public PachimonFigureDto SelfFigure;
        public PachimonFigureDto OpponentFigure;
        public List<CommandDto> Commands;
    }
}
