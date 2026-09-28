using Atlas.Domain;
using Atlas.MasterData.Enums;

namespace Atlas.Presentation.Battle
{
    public sealed record MoveEffectDto
    {
        public bool AttackerIsSelf;
        // 物理は体当たり、特殊はビーム。
        public MoveCategory Category;
        public PachimonType MoveType;
        public bool Hit;
        public bool Critical;
        public EffectivenessResult Effectiveness;
    }
}
