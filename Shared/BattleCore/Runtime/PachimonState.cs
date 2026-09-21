using System;
using System.Collections.Generic;

namespace Atlas.BattleCore
{
    // 選出された1体の対戦中の状態(現在HP等)。Statsは実効ステータス(生成時点で確定、
    // 交代を跨いでも変化しない=ランク変動要素はミニマム版に無いため)。
    public sealed class PachimonState
    {
        public ParticipantStats Stats { get; }
        public IReadOnlyList<MoveData> Moves { get; }
        public int CurrentHp { get; private set; }
        public bool IsFainted => CurrentHp <= 0;

        private readonly int[] _currentPp;
        public IReadOnlyList<int> CurrentPp => _currentPp;

        public PachimonState(ParticipantStats stats, IReadOnlyList<MoveData> moves)
        {
            if (moves.Count == 0)
            {
                throw new ArgumentException("習得技が1つも無いパチモンは選出できません。", nameof(moves));
            }

            Stats = stats;
            Moves = moves;
            CurrentHp = stats.Hp;

            _currentPp = new int[moves.Count];
            for (int i = 0; i < moves.Count; i++)
            {
                _currentPp[i] = moves[i].MaxPp;
            }
        }

        public void ApplyDamage(int amount)
        {
            CurrentHp = Math.Max(0, CurrentHp - amount);
        }

        // 技使用時にPPを1消費する。命中/外れ/状態技に関わらず、技を選択した時点で消費する
        // (本家ポケモンの挙動に準拠)。PP0の技を選ばせないのは呼び出し側(UI)の責務であり、
        // それでもPP0の技が渡された場合はSwitchToが瀕死のパーティメンバーを拒否するのと
        // 同じ方針でフェイルファストする。
        internal void ConsumeMovePp(int moveIndex)
        {
            if (_currentPp[moveIndex] <= 0)
            {
                throw new ArgumentException("PPが残っていない技は選択できません。", nameof(moveIndex));
            }

            _currentPp[moveIndex]--;
        }
    }
}
