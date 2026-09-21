namespace Atlas.BattleCore
{
    // 命中判定・急所判定・ダメージ乱数(0.85〜1.00)で使う乱数を注入するためのインターフェース。
    // テスト時はシード固定・決定論的な実装に差し替える(docs/design/battle.md参照)。
    public interface IRandomSource
    {
        // [minInclusive, maxInclusive]の範囲で整数を返す。
        int NextInt(int minInclusive, int maxInclusive);

        // [minInclusive, maxInclusive]の範囲で浮動小数を返す。
        double NextDouble(double minInclusive, double maxInclusive);
    }

    // 本番用のデフォルト実装。System.Randomをラップするだけ。
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly System.Random _random;

        public SystemRandomSource() : this(new System.Random())
        {
        }

        public SystemRandomSource(System.Random random)
        {
            _random = random;
        }

        public int NextInt(int minInclusive, int maxInclusive) => _random.Next(minInclusive, maxInclusive + 1);

        public double NextDouble(double minInclusive, double maxInclusive) =>
            minInclusive + _random.NextDouble() * (maxInclusive - minInclusive);
    }
}
