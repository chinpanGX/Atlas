using System.Collections.Generic;
using Atlas.BattleCore;
using MasterMemory;

namespace Atlas.Infrastructure.Mock
{
    // type_chartマスタからAtlas.BattleCore.ITypeChartを構築する。単体タイプ同士の組み合わせしか
    // 持たない(複合タイプの掛け合わせはDamageCalculator側が2回引いて計算する、
    // design/architecture.md参照)。
    public sealed class MasterDataTypeChart : ITypeChart
    {
        private readonly Dictionary<(ElementType Attack, ElementType Defend), EffectivenessResult> table;

        public MasterDataTypeChart(MemoryDatabase database)
        {
            table = new Dictionary<(ElementType, ElementType), EffectivenessResult>();
            foreach (var row in database.TypeChartDataTable.All)
            {
                var key = (MasterDataConversions.ToElementType(row.AttackType), MasterDataConversions.ToElementType(row.DefendType));
                table[key] = MasterDataConversions.ToEffectivenessResult(row.Effectiveness);
            }
        }

        public EffectivenessResult GetEffectiveness(ElementType attackType, ElementType defendType) =>
            table[(attackType, defendType)];
    }
}
