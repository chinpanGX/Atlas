using System;

namespace Atlas.BattleCore
{
    // ダメージ計算Section(攻撃力決定/防御力決定/ダメージ算出)。
    public static class DamageCalculator
    {
        // 急所発生率: 1/16固定(ランク変動要素は無い)。
        private const int CriticalDenominator = 16;
        private const double CriticalMultiplier = 1.5;
        private const double StabMultiplier = 1.5;
        private const double RandomMin = 0.85;
        private const double RandomMax = 1.00;

        public static DamageResult Calculate(
            ParticipantStats attacker,
            ParticipantStats defender,
            MoveData move,
            ITypeChart typeChart,
            IRandomSource random)
        {
            bool critical = random.NextInt(1, CriticalDenominator) == 1;
            double rawTypeMultiplier = ResolveRawTypeMultiplier(move.MoveType, defender, typeChart);
            var effectiveness = BucketEffectiveness(rawTypeMultiplier);

            if (rawTypeMultiplier == 0d)
            {
                // 無効: 命中判定を通っていても必ずダメージ0(タイプ相性は乱数・STAB・急所より優先)
                return new DamageResult(0, critical, effectiveness);
            }

            int a = move.Category == MoveCategory.Physical ? attacker.Atk : attacker.SpAtk;
            int d = move.Category == MoveCategory.Physical ? defender.Def : defender.SpDef;

            // floor(floor(floor(2*level/5+2) * base_power * A / D) / 50 + 2)。
            // 本家同様、各段階を整数演算(切り捨て)で行う。
            long step1 = 2L * attacker.Level / 5 + 2;
            long step2 = step1 * move.BasePower * a;
            long step3 = step2 / d;
            long baseDamage = step3 / 50 + 2;

            bool isStab = move.MoveType == attacker.PrimaryType || move.MoveType == attacker.SecondaryType;
            double stab = isStab ? StabMultiplier : 1.0;
            double criticalMultiplier = critical ? CriticalMultiplier : 1.0;
            double randomFactor = random.NextDouble(RandomMin, RandomMax);

            double multiplier = stab * rawTypeMultiplier * criticalMultiplier * randomFactor;
            int damage = (int)Math.Floor(baseDamage * multiplier);

            // 本家同様、無効でない限り最低保証ダメージ1。
            damage = Math.Max(1, damage);

            return new DamageResult(damage, critical, effectiveness);
        }

        // 単体タイプごとの倍率(0/0.5/1/2)を2回掛け合わせる。取り得る値は0/0.25/0.5/1/2/4。
        private static double ResolveRawTypeMultiplier(ElementType moveType, ParticipantStats defender, ITypeChart typeChart)
        {
            double multiplier = ToMultiplier(typeChart.GetEffectiveness(moveType, defender.PrimaryType));
            if (defender.SecondaryType is { } secondaryType)
            {
                multiplier *= ToMultiplier(typeChart.GetEffectiveness(moveType, secondaryType));
            }

            return multiplier;
        }

        private static double ToMultiplier(EffectivenessResult effectiveness) => effectiveness switch
        {
            EffectivenessResult.Immune => 0.0,
            EffectivenessResult.DoubleNotVeryEffective => 0.25,
            EffectivenessResult.NotVeryEffective => 0.5,
            EffectivenessResult.Normal => 1.0,
            EffectivenessResult.SuperEffective => 2.0,
            EffectivenessResult.DoubleSuperEffective => 4.0,
            _ => throw new ArgumentOutOfRangeException(nameof(effectiveness), effectiveness, null),
        };

        // 倍率を表示・報告用の段階に分ける。0.25倍・4倍は「超いまひとつ」「超バツグン」と表示するため
        // 0.5倍・2倍とは別の値にする。
        private static EffectivenessResult BucketEffectiveness(double rawMultiplier)
        {
            return rawMultiplier switch
            {
                0d => EffectivenessResult.Immune,
                < 0.5 => EffectivenessResult.DoubleNotVeryEffective,
                < 1.0 => EffectivenessResult.NotVeryEffective,
                > 2.0 => EffectivenessResult.DoubleSuperEffective,
                > 1.0 => EffectivenessResult.SuperEffective,
                _ => EffectivenessResult.Normal,
            };
        }
    }

    public sealed record DamageResult(int Damage, bool Critical, EffectivenessResult Effectiveness);
}
