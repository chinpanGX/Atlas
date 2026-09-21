using System.Collections.Generic;

namespace Atlas.BattleCore.Tests
{
    // テスト用のITypeChart実装。指定した組み合わせ以外は全てNormal(等倍)を返す。
    internal sealed class StaticTypeChart : ITypeChart
    {
        private readonly Dictionary<(ElementType Attack, ElementType Defend), EffectivenessResult> _table = new();

        public StaticTypeChart With(ElementType attack, ElementType defend, EffectivenessResult effectiveness)
        {
            _table[(attack, defend)] = effectiveness;
            return this;
        }

        public EffectivenessResult GetEffectiveness(ElementType attackType, ElementType defendType) =>
            _table.TryGetValue((attackType, defendType), out var result) ? result : EffectivenessResult.Normal;
    }
}
