namespace Atlas.BattleCore
{
    // Domain.MasterDataのPachimonTypeとは独立(Atlas.BattleCoreはmaster-data-pipeline生成型に
    // 依存しない)。呼び出し側がPachimonTypeからこの型へ変換する。NONE相当はSecondaryTypeを
    // nullにすることで表現するため、ここには含まない。
    public enum ElementType
    {
        Normal,
        Fire,
        Water,
        Electric,
        Grass,
        Ice,
        Fighting,
        Poison,
        Ground,
        Flying,
        Psychic,
        Bug,
        Rock,
        Ghost,
        Dragon,
        Dark,
        Steel,
        Fairy,
    }
}
