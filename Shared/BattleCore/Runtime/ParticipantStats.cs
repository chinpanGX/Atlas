namespace Atlas.BattleCore
{
    // マスタ+プレイヤー所持データから算出済みの実効ステータス。base/IV/EVからの算出は
    // 呼び出し側(Client Mock/バトルサーバーのHub実装)の責務であり、Atlas.BattleCoreは
    // 算出済みの値のみを扱う(docs/design/battle.md「共通モジュール」参照)。
    public readonly record struct ParticipantStats(
        int Level,
        int Hp,
        int Atk,
        int Def,
        int SpAtk,
        int SpDef,
        int Speed,
        ElementType PrimaryType,
        ElementType? SecondaryType);
}
