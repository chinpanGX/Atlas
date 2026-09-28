namespace Atlas.BattleCore
{
    // type_chartマスタのeffectiveness(ENUM)とは別物。ITypeChartから得た単体タイプ同士の
    // 判定を組み合わせた「最終的な効果の程度」をAtlas.BattleCoreが表現するための型。
    // BattleContractsのActionResultでもそのまま使う。
    // DoubleNotVeryEffective(0.25倍)・DoubleSuperEffective(4倍)は複合タイプの掛け合わせでのみ生じ、
    // ITypeChart(単体タイプ同士)が返すことはない。
    public enum EffectivenessResult
    {
        Immune,
        DoubleNotVeryEffective,
        NotVeryEffective,
        Normal,
        SuperEffective,
        DoubleSuperEffective,
    }
}
