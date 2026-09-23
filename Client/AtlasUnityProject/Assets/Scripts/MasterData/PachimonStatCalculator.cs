namespace Atlas.MasterData
{
    // 種族値から実効ステータスを求める(design/battle.md「実効ステータス計算」)。IVは無く、
    // 努力値は現状全て0、レベルは50固定のため、種族値だけで決まる。
    public static class PachimonStatCalculator
    {
        public const int FixedLevel = 50;

        public static int CalculateHp(int baseHp)
        {
            return 2 * baseHp * FixedLevel / 100 + FixedLevel + 10;
        }

        public static int CalculateOther(int baseStat)
        {
            return 2 * baseStat * FixedLevel / 100 + 5;
        }
    }
}
