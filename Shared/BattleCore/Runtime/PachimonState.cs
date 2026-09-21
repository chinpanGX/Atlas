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

        public PachimonState(ParticipantStats stats, IReadOnlyList<MoveData> moves)
        {
            if (moves.Count == 0)
            {
                throw new ArgumentException("習得技が1つも無いパチモンは選出できません。", nameof(moves));
            }

            Stats = stats;
            Moves = moves;
            CurrentHp = stats.Hp;
        }

        public void ApplyDamage(int amount)
        {
            CurrentHp = Math.Max(0, CurrentHp - amount);
        }
    }
}
