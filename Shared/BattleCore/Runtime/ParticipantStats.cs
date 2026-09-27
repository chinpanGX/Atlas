namespace Atlas.BattleCore
{
    // マスタ+プレイヤー所持データから算出済みの実効ステータス。種族値・努力値からの算出は
    // 呼び出し側(Client Mock/バトルサーバーのLoadoutBuilder)の責務であり、Atlas.BattleCoreは
    // 算出済みの値のみを扱う。
    public sealed record ParticipantStats(
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
