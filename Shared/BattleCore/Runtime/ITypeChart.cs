namespace Atlas.BattleCore
{
    // type_chartマスタ(単体タイプ同士のみ)の読み込みを抽象化する。実装(マスタからの読み込み)は
    // 呼び出し側(Client Mock/バトルサーバー)が注入する。複合タイプ(secondary_type)の掛け合わせは
    // Atlas.BattleCore(DamageCalculator)側が2回引いて計算する(docs/design/architecture.md参照)。
    public interface ITypeChart
    {
        EffectivenessResult GetEffectiveness(ElementType attackType, ElementType defendType);
    }
}
