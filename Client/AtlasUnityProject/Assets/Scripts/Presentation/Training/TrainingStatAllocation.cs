using System;
using Atlas.Domain;

namespace Atlas.Presentation.Training
{
    // ステータスポイントの割り振りの、確定するまでの調整。確定するまでは通信せず、ここで持つ値だけを変える。
    // 能力はHP・こうげき・ぼうぎょ・とくこう・とくぼう・すばやさの順に0〜5の番号で扱う。
    public sealed class TrainingStatAllocation
    {
        public const int StatCount = 6;

        private readonly int[] committed;
        private readonly int[] adjusted;

        public TrainingStatAllocation(StatPoints committedPoints)
        {
            committed = ToArray(committedPoints);
            adjusted = (int[])committed.Clone();
        }

        public int RemainingPoints => StatPoints.MaxTotal - Sum(adjusted);

        // 確定するときに使う育成チケットの枚数。増やしたポイントの合計で、減らした分は差し引かない。
        public int RequiredTickets
        {
            get
            {
                var total = 0;
                for (var index = 0; index < StatCount; index++)
                {
                    total += Math.Max(0, adjusted[index] - committed[index]);
                }
                return total;
            }
        }

        public bool IsDirty
        {
            get
            {
                for (var index = 0; index < StatCount; index++)
                {
                    if (adjusted[index] != committed[index])
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public int GetCommitted(int stat) => committed[stat];

        public int GetAdjusted(int stat) => adjusted[stat];

        public bool CanIncrease(int stat) => RemainingPoints > 0;

        public bool CanDecrease(int stat) => adjusted[stat] > 0;

        public void Increase(int stat)
        {
            if (CanIncrease(stat))
            {
                adjusted[stat]++;
            }
        }

        public void Decrease(int stat)
        {
            if (CanDecrease(stat))
            {
                adjusted[stat]--;
            }
        }

        public void Revert()
        {
            Array.Copy(committed, adjusted, StatCount);
        }

        public StatPoints ToAdjustedStatPoints()
        {
            return new StatPoints(adjusted[0], adjusted[1], adjusted[2], adjusted[3], adjusted[4], adjusted[5]);
        }

        private static int[] ToArray(StatPoints points)
        {
            return new[] { points.Hp, points.Atk, points.Def, points.Spatk, points.Spdef, points.Speed };
        }

        private static int Sum(int[] values)
        {
            var total = 0;
            foreach (var value in values)
            {
                total += value;
            }
            return total;
        }
    }
}
