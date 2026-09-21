namespace Atlas.BattleCore
{
    // type_chartマスタのeffectiveness(ENUM)とは別物。ITypeChartから得た単体タイプ同士の
    // 判定を組み合わせた「最終的な効果の程度」をAtlas.BattleCoreが表現するための型
    // (docs/design/battle.md「IBattleConnection / Payload定義」参照)。
    public enum EffectivenessResult
    {
        Immune,
        NotVeryEffective,
        Normal,
        SuperEffective,
    }
}
