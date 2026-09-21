using System.Collections.Generic;

namespace Atlas.BattleCore.Tests
{
    // テスト用の決定論的IRandomSource実装。あらかじめ用意した値をキューから順に返す。
    // 用意した数を超えて呼ばれた場合は最後の値を返し続ける(テストの安定性優先)。
    internal sealed class FixedRandomSource : IRandomSource
    {
        private readonly Queue<int> _ints;
        private readonly Queue<double> _doubles;
        private int _lastInt;
        private double _lastDouble;

        public FixedRandomSource(IEnumerable<int>? ints = null, IEnumerable<double>? doubles = null)
        {
            _ints = new Queue<int>(ints ?? new int[0]);
            _doubles = new Queue<double>(doubles ?? new double[0]);
        }

        public int NextInt(int minInclusive, int maxInclusive)
        {
            _lastInt = _ints.Count > 0 ? _ints.Dequeue() : _lastInt;
            return _lastInt;
        }

        public double NextDouble(double minInclusive, double maxInclusive)
        {
            _lastDouble = _doubles.Count > 0 ? _doubles.Dequeue() : _lastDouble;
            return _lastDouble;
        }
    }
}
