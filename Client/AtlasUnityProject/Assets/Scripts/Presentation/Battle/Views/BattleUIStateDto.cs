using System.Collections.Generic;

namespace Atlas.Presentation.Battle
{
    public sealed record BattleUIStateDto
    {
        public SelfInfoDto SelfInfo;
        public OpponentInfoDto OpponentInfo;
        public List<CommandDto> Commands;
    }
}
