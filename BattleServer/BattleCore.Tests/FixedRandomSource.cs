using System.Collections.Generic;

namespace Atlas.BattleCore.Tests
{
    // テスト用の決定論的IRandomSource実装。あらかじめ用意した値をキューから順に返す。
    // 用意した数を超えて呼ばれた場合は最後の値を返し続ける(テストの安定性優先)。
    internal sealed class FixedRandomSource : IRandomSource
    {
        private readonly Queue<int> ints;
        private readonly Queue<double> doubles;
        private int lastInt;
        private double lastDouble;

        public FixedRandomSource(IEnumerable<int>? ints = null, IEnumerable<double>? doubles = null)
        {
            this.ints = new Queue<int>(ints ?? new int[0]);
            this.doubles = new Queue<double>(doubles ?? new double[0]);
        }

        public int NextInt(int minInclusive, int maxInclusive)
        {
            lastInt = ints.Count > 0 ? ints.Dequeue() : lastInt;
            return lastInt;
        }

        public double NextDouble(double minInclusive, double maxInclusive)
        {
            lastDouble = doubles.Count > 0 ? doubles.Dequeue() : lastDouble;
            return lastDouble;
        }
    }
}
