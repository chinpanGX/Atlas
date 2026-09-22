using System;
using Atlas.BattleCore;
using Atlas.MasterData.Enums;

namespace Atlas.Infrastructure.Mock
{
    // Domain.MasterData(PachimonType/MoveCategory/TypeEffectiveness)とAtlas.BattleCore
    // (ElementType/MoveCategory/EffectivenessResult)の間の型変換。design/battle.md
    // 「実効ステータス計算」参照。この変換はAtlas.BattleCoreに依存しない呼び出し側
    // (Client Mock)の責務であり、TestPartyFactory/MasterDataTypeChartから共通で使う。
    internal static class MasterDataConversions
    {
        public static ElementType ToElementType(PachimonType type) => type switch
        {
            PachimonType.Normal => ElementType.Normal,
            PachimonType.Fire => ElementType.Fire,
            PachimonType.Water => ElementType.Water,
            PachimonType.Electric => ElementType.Electric,
            PachimonType.Grass => ElementType.Grass,
            PachimonType.Ice => ElementType.Ice,
            PachimonType.Fighting => ElementType.Fighting,
            PachimonType.Poison => ElementType.Poison,
            PachimonType.Ground => ElementType.Ground,
            PachimonType.Flying => ElementType.Flying,
            PachimonType.Psychic => ElementType.Psychic,
            PachimonType.Bug => ElementType.Bug,
            PachimonType.Rock => ElementType.Rock,
            PachimonType.Ghost => ElementType.Ghost,
            PachimonType.Dragon => ElementType.Dragon,
            PachimonType.Dark => ElementType.Dark,
            PachimonType.Steel => ElementType.Steel,
            PachimonType.Fairy => ElementType.Fairy,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type,
                "PachimonType.Noneはprimary_type/move_typeには使えません。"),
        };

        // secondary_typeのNone(=セカンドタイプ無し)はnullで表現する。
        public static ElementType? ToSecondaryElementType(PachimonType type) =>
            type == PachimonType.None ? null : ToElementType(type);

        public static Atlas.BattleCore.MoveCategory ToMoveCategory(Atlas.MasterData.Enums.MoveCategory category) => category switch
        {
            Atlas.MasterData.Enums.MoveCategory.Physical => Atlas.BattleCore.MoveCategory.Physical,
            Atlas.MasterData.Enums.MoveCategory.Special => Atlas.BattleCore.MoveCategory.Special,
            Atlas.MasterData.Enums.MoveCategory.Status => Atlas.BattleCore.MoveCategory.Status,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        public static EffectivenessResult ToEffectivenessResult(TypeEffectiveness effectiveness) => effectiveness switch
        {
            TypeEffectiveness.Immune => EffectivenessResult.Immune,
            TypeEffectiveness.NotVeryEffective => EffectivenessResult.NotVeryEffective,
            TypeEffectiveness.Normal => EffectivenessResult.Normal,
            TypeEffectiveness.SuperEffective => EffectivenessResult.SuperEffective,
            _ => throw new ArgumentOutOfRangeException(nameof(effectiveness), effectiveness, null),
        };
    }
}
