using NUnit.Framework;

namespace Atlas.BattleCore.Tests
{
    public class DamageCalculatorTests
    {
        private static ParticipantStats MakeStats(
            int atk = 100, int def = 100, ElementType primary = ElementType.Water, ElementType? secondary = null) =>
            new(Level: 50, Hp: 150, Atk: atk, Def: def, SpAtk: atk, SpDef: def, Speed: 50,
                PrimaryType: primary, SecondaryType: secondary);

        private static MoveData MakeMove(
            ElementType moveType = ElementType.Normal, MoveCategory category = MoveCategory.Physical,
            int basePower = 80, int accuracy = 100) =>
            new(moveType, category, basePower, accuracy);

        [Test]
        public void Calculate_NoStabNoCritNormalType_MatchesBaseFormula()
        {
            var attacker = MakeStats(atk: 100, primary: ElementType.Water);
            var defender = MakeStats(def: 100, primary: ElementType.Fire);
            var move = MakeMove(moveType: ElementType.Normal, basePower: 80);
            var random = new FixedRandomSource(ints: new[] { 5 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart();

            // floor(floor(floor(2*50/5+2)*80*100/100)/50+2) = floor(floor(22*80)/50+2) = 37
            var result = DamageCalculator.Calculate(attacker, defender, move, typeChart, random);

            Assert.AreEqual(37, result.Damage);
            Assert.IsFalse(result.Critical);
            Assert.AreEqual(EffectivenessResult.Normal, result.Effectiveness);
        }

        [Test]
        public void Calculate_StabApplies_MultipliesBy1Point5()
        {
            var attacker = MakeStats(atk: 100, primary: ElementType.Normal);
            var defender = MakeStats(def: 100, primary: ElementType.Fire);
            var move = MakeMove(moveType: ElementType.Normal, basePower: 80);
            var random = new FixedRandomSource(ints: new[] { 5 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart();

            var result = DamageCalculator.Calculate(attacker, defender, move, typeChart, random);

            Assert.AreEqual(55, result.Damage); // floor(37 * 1.5)
        }

        [Test]
        public void Calculate_CriticalHit_MultipliesBy1Point5()
        {
            var attacker = MakeStats(atk: 100, primary: ElementType.Water);
            var defender = MakeStats(def: 100, primary: ElementType.Fire);
            var move = MakeMove(moveType: ElementType.Normal, basePower: 80);
            var random = new FixedRandomSource(ints: new[] { 1 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart();

            var result = DamageCalculator.Calculate(attacker, defender, move, typeChart, random);

            Assert.IsTrue(result.Critical);
            Assert.AreEqual(55, result.Damage); // floor(37 * 1.5)
        }

        [Test]
        public void Calculate_SuperEffectiveSingleType_MultipliesBy2()
        {
            var attacker = MakeStats(atk: 100, primary: ElementType.Normal); // moveTypeと不一致(STABなし)
            var defender = MakeStats(def: 100, primary: ElementType.Fire);
            var move = MakeMove(moveType: ElementType.Water, basePower: 80);
            var random = new FixedRandomSource(ints: new[] { 5 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart().With(ElementType.Water, ElementType.Fire, EffectivenessResult.SuperEffective);

            var result = DamageCalculator.Calculate(attacker, defender, move, typeChart, random);

            Assert.AreEqual(EffectivenessResult.SuperEffective, result.Effectiveness);
            Assert.AreEqual(74, result.Damage); // floor(37 * 2.0)
        }

        [Test]
        public void Calculate_ImmuneSingleType_DamageIsZero()
        {
            var attacker = MakeStats(atk: 100, primary: ElementType.Normal);
            var defender = MakeStats(def: 100, primary: ElementType.Ghost);
            var move = MakeMove(moveType: ElementType.Normal, basePower: 80);
            var random = new FixedRandomSource(ints: new[] { 5 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart().With(ElementType.Normal, ElementType.Ghost, EffectivenessResult.Immune);

            var result = DamageCalculator.Calculate(attacker, defender, move, typeChart, random);

            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(EffectivenessResult.Immune, result.Effectiveness);
        }

        [Test]
        public void Calculate_DualTypeEffectivenessCancelsOut_BucketsAsNormal()
        {
            var attacker = MakeStats(atk: 100, primary: ElementType.Normal); // moveTypeと不一致(STABなし)
            var defender = MakeStats(def: 100, primary: ElementType.Steel, secondary: ElementType.Flying);
            var move = MakeMove(moveType: ElementType.Fighting, basePower: 80);
            var random = new FixedRandomSource(ints: new[] { 5 }, doubles: new[] { 1.0 });
            // Fighting->Steel: SuperEffective(x2), Fighting->Flying: NotVeryEffective(x0.5) => 合計等倍
            var typeChart = new StaticTypeChart()
                .With(ElementType.Fighting, ElementType.Steel, EffectivenessResult.SuperEffective)
                .With(ElementType.Fighting, ElementType.Flying, EffectivenessResult.NotVeryEffective);

            var result = DamageCalculator.Calculate(attacker, defender, move, typeChart, random);

            Assert.AreEqual(EffectivenessResult.Normal, result.Effectiveness);
            Assert.AreEqual(37, result.Damage);
        }

        [Test]
        public void Calculate_DualTypeBothSuperEffective_BucketsAsSuperEffective()
        {
            var attacker = MakeStats(atk: 100, primary: ElementType.Normal); // moveTypeと不一致(STABなし)
            var defender = MakeStats(def: 100, primary: ElementType.Fire, secondary: ElementType.Rock);
            var move = MakeMove(moveType: ElementType.Ground, basePower: 80);
            var random = new FixedRandomSource(ints: new[] { 5 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart()
                .With(ElementType.Ground, ElementType.Fire, EffectivenessResult.SuperEffective)
                .With(ElementType.Ground, ElementType.Rock, EffectivenessResult.SuperEffective);

            var result = DamageCalculator.Calculate(attacker, defender, move, typeChart, random);

            Assert.AreEqual(EffectivenessResult.SuperEffective, result.Effectiveness);
            Assert.AreEqual(148, result.Damage); // floor(37 * 2.0 * 2.0)
        }

        [Test]
        public void Calculate_DamageBelowOne_IsClampedToMinimumOne()
        {
            var attacker = new ParticipantStats(Level: 1, Hp: 20, Atk: 1, Def: 1, SpAtk: 1, SpDef: 1, Speed: 1,
                PrimaryType: ElementType.Water, SecondaryType: null);
            var defender = new ParticipantStats(Level: 1, Hp: 20, Atk: 1, Def: 255, SpAtk: 1, SpDef: 255, Speed: 1,
                PrimaryType: ElementType.Fire, SecondaryType: null);
            var move = MakeMove(moveType: ElementType.Normal, basePower: 1);
            var random = new FixedRandomSource(ints: new[] { 5 }, doubles: new[] { 0.85 });
            var typeChart = new StaticTypeChart().With(ElementType.Normal, ElementType.Fire, EffectivenessResult.NotVeryEffective);

            var result = DamageCalculator.Calculate(attacker, defender, move, typeChart, random);

            Assert.AreEqual(1, result.Damage);
        }
    }
}
