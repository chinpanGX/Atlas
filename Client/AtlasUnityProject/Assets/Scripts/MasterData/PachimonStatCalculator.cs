namespace Atlas.MasterData
{
    // 種族値と割り振ったステータスポイントから実効ステータスを求める(画面表示・Mock対戦用)。BattleServerのLoadoutBuilderと同じ式。
    // IVは無く、レベルは50固定。ステータスポイントは能力の値にそのまま足す(省略したときは割り振り無し)。
    public static class PachimonStatCalculator
    {
        public const int FixedLevel = 50;

        public static int CalculateHp(int baseHp, int statPoints = 0)
        {
            return 2 * baseHp * FixedLevel / 100 + FixedLevel + 10 + statPoints;
        }

        public static int CalculateOther(int baseStat, int statPoints = 0)
        {
            return 2 * baseStat * FixedLevel / 100 + 5 + statPoints;
        }
    }
}
