using System.Collections.Generic;
using Atlas.Domain;
using Atlas.MasterData.Enums;
using MasterMemory;

namespace Atlas.Presentation.Battle
{
    // type_chartマスタ(単体タイプ同士の相性)から、技のタイプと相手のタイプ(1つまたは2つ)の組み合わせの効果を求める。
    // 技を選ぶ前の目安の表示用で、判定そのものはBattleServerが行う。複合タイプは単体タイプごとの倍率を
    // 掛け合わせる(Atlas.BattleCoreのダメージ計算と同じ)。
    public sealed class TypeMatchupTable
    {
        private readonly Dictionary<(PachimonType Attack, PachimonType Defend), TypeEffectiveness> table = new();

        public TypeMatchupTable(MemoryDatabase database)
        {
            foreach (var row in database.TypeChartDataTable.All)
            {
                table[(row.AttackType, row.DefendType)] = row.Effectiveness;
            }
        }

        public EffectivenessResult Resolve(PachimonType moveType, PachimonType primaryType, PachimonType secondaryType)
        {
            var multiplier = ToMultiplier(table[(moveType, primaryType)]);
            if (secondaryType != PachimonType.None)
            {
                multiplier *= ToMultiplier(table[(moveType, secondaryType)]);
            }

            return multiplier switch
            {
                0d => EffectivenessResult.Immune,
                < 0.5 => EffectivenessResult.DoubleNotVeryEffective,
                < 1.0 => EffectivenessResult.NotVeryEffective,
                > 2.0 => EffectivenessResult.DoubleSuperEffective,
                > 1.0 => EffectivenessResult.SuperEffective,
                _ => EffectivenessResult.Normal,
            };
        }

        private static double ToMultiplier(TypeEffectiveness effectiveness) => effectiveness switch
        {
            TypeEffectiveness.Immune => 0.0,
            TypeEffectiveness.NotVeryEffective => 0.5,
            TypeEffectiveness.SuperEffective => 2.0,
            _ => 1.0,
        };
    }
}
