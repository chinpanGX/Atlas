using System.Linq;
using NUnit.Framework;

namespace Atlas.BattleCore.Tests
{
    public class BattleEngineTests
    {
        private static ParticipantStats MakeStats(int hp, int atk, int def, int speed) =>
            new(Level: 50, Hp: hp, Atk: atk, Def: def, SpAtk: atk, SpDef: def, Speed: speed,
                PrimaryType: ElementType.Normal, SecondaryType: null);

        private static PachimonState MakeMon(int hp, int atk = 50, int def = 50, int speed = 50, int accuracy = 100) =>
            new(MakeStats(hp, atk, def, speed),
                new[] { new MoveData(ElementType.Normal, MoveCategory.Physical, BasePower: 40, Accuracy: accuracy) });

        [Test]
        public void ProcessTurn_BothUseMoves_FasterActsFirstAndDamageApplied()
        {
            var p1 = new BattleSide(new[] { MakeMon(hp: 100, atk: 60, speed: 100) });
            var p2 = new BattleSide(new[] { MakeMon(hp: 100, def: 30, speed: 10) });
            var state = new BattleState(p1, p2);
            // crit roll(p1) -> 5(no crit), random factor(p1) -> 1.0, crit roll(p2)/random(p2)なし(defenderが瀕死しない限り反撃自体は別行動として処理される)
            var random = new FixedRandomSource(ints: new[] { 5, 100, 5 }, doubles: new[] { 1.0, 1.0 });
            var typeChart = new StaticTypeChart();

            var result = BattleEngine.ProcessTurn(state, PlayerAction.UseMove(0), PlayerAction.UseMove(0), typeChart, random);

            Assert.AreEqual(1, result.TurnNumber);
            Assert.AreEqual(2, result.Actions.Count);
            Assert.AreEqual(BattleSideId.Player1, result.Actions[0].Side); // p1の方が速い
            Assert.IsTrue(result.Actions[0].DamageDealt > 0);
            Assert.IsNull(result.BattleEnd);
        }

        [Test]
        public void ProcessTurn_AccuracyRollAboveThreshold_Misses()
        {
            var p1 = new BattleSide(new[] { MakeMon(hp: 100, speed: 100, accuracy: 50) });
            var p2 = new BattleSide(new[] { MakeMon(hp: 100, speed: 10) });
            var state = new BattleState(p1, p2);
            var random = new FixedRandomSource(ints: new[] { 51 }, doubles: new double[0]);
            var typeChart = new StaticTypeChart();

            var result = BattleEngine.ProcessTurn(state, PlayerAction.UseMove(0), null, typeChart, random);

            var outcome = result.Actions.Single(a => a.Side == BattleSideId.Player1);
            Assert.IsFalse(outcome.Hit);
            Assert.AreEqual(0, outcome.DamageDealt);
            Assert.AreEqual(100, p2.Active.CurrentHp);
        }

        [Test]
        public void ProcessTurn_TargetFaintsButPartyRemains_RequiresForcedSwitchNextTurn()
        {
            var p1 = new BattleSide(new[] { MakeMon(hp: 100, atk: 999, speed: 100) });
            var p2 = new BattleSide(new[] { MakeMon(hp: 5, def: 1, speed: 10), MakeMon(hp: 100, speed: 10) });
            var state = new BattleState(p1, p2);
            var random = new FixedRandomSource(ints: new[] { 5, 100 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart();

            var result = BattleEngine.ProcessTurn(state, PlayerAction.UseMove(0), PlayerAction.UseMove(0), typeChart, random);

            var outcome = result.Actions.Single(a => a.Side == BattleSideId.Player1);
            Assert.IsTrue(outcome.TargetFainted);
            Assert.IsNull(result.BattleEnd);
            Assert.IsTrue(p2.RequiresForcedSwitch);
            CollectionAssert.Contains(result.PlayersRequiringForcedSwitch, BattleSideId.Player2);
        }

        [Test]
        public void ProcessTurn_LastPachimonFaints_EndsBattleImmediatelyWithoutProcessingRemainingActions()
        {
            var p1 = new BattleSide(new[] { MakeMon(hp: 100, atk: 999, speed: 100) });
            var p2 = new BattleSide(new[] { MakeMon(hp: 5, def: 1, speed: 10) });
            var state = new BattleState(p1, p2);
            var random = new FixedRandomSource(ints: new[] { 5, 100 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart();

            var result = BattleEngine.ProcessTurn(state, PlayerAction.UseMove(0), PlayerAction.UseMove(0), typeChart, random);

            Assert.IsNotNull(result.BattleEnd);
            Assert.AreEqual(BattleSideId.Player1, result.BattleEnd!.Winner);
            Assert.AreEqual(BattleEndReason.AllFainted, result.BattleEnd.Reason);
            // 既に決着が付いたp2側の行動は処理されない
            Assert.AreEqual(1, result.Actions.Count);
            Assert.IsEmpty(result.PlayersRequiringForcedSwitch);
        }

        [Test]
        public void ProcessTurn_ForcedSwitchRequiredButMoveSubmitted_IsSkipped()
        {
            var p1 = new BattleSide(new[] { MakeMon(hp: 100, speed: 100) });
            var p2 = new BattleSide(new[] { MakeMon(hp: 5), MakeMon(hp: 100) }) { RequiresForcedSwitch = true };
            var state = new BattleState(p1, p2);
            var random = new FixedRandomSource(ints: new[] { 5, 100 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart();

            var result = BattleEngine.ProcessTurn(state, PlayerAction.UseMove(0), PlayerAction.UseMove(0), typeChart, random);

            var p2Outcome = result.Actions.Single(a => a.Side == BattleSideId.Player2);
            Assert.AreEqual(BattleActionKind.Skip, p2Outcome.Kind);
            Assert.IsTrue(p2.RequiresForcedSwitch); // 未解消のまま持ち越し
        }

        [Test]
        public void ProcessTurn_ForcedSwitchResolvedWithSwitch_ClearsFlagAndReportsNewActiveIndex()
        {
            var p1 = new BattleSide(new[] { MakeMon(hp: 100, speed: 100) });
            var p2 = new BattleSide(new[] { MakeMon(hp: 5), MakeMon(hp: 100) }) { RequiresForcedSwitch = true };
            var state = new BattleState(p1, p2);
            var random = new FixedRandomSource(ints: new[] { 5, 100 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart();

            var result = BattleEngine.ProcessTurn(state, PlayerAction.UseMove(0), PlayerAction.SwitchTo(1), typeChart, random);

            var p2Outcome = result.Actions.Single(a => a.Side == BattleSideId.Player2);
            Assert.AreEqual(BattleActionKind.Switch, p2Outcome.Kind);
            Assert.AreEqual(1, p2Outcome.NewActiveIndex);
            Assert.IsFalse(p2.RequiresForcedSwitch);
            Assert.AreEqual(1, p2.ActiveIndex);
        }

        [Test]
        public void ProcessTurn_StatusMove_DealsNoDamage()
        {
            var p1 = new BattleSide(new[]
            {
                new PachimonState(MakeStats(100, 50, 50, 100),
                    new[] { new MoveData(ElementType.Normal, MoveCategory.Status, BasePower: 0, Accuracy: 100) }),
            });
            var p2 = new BattleSide(new[] { MakeMon(hp: 100, speed: 10) });
            var state = new BattleState(p1, p2);
            var random = new FixedRandomSource(ints: new[] { 100, 5 }, doubles: new[] { 1.0 });
            var typeChart = new StaticTypeChart();

            var result = BattleEngine.ProcessTurn(state, PlayerAction.UseMove(0), null, typeChart, random);

            var outcome = result.Actions.Single(a => a.Side == BattleSideId.Player1);
            Assert.IsTrue(outcome.Hit);
            Assert.AreEqual(0, outcome.DamageDealt);
            Assert.AreEqual(100, p2.Active.CurrentHp);
        }

        [Test]
        public void ProcessTurn_BothSidesSkip_ReportsSkipForBoth()
        {
            var p1 = new BattleSide(new[] { MakeMon(hp: 100) });
            var p2 = new BattleSide(new[] { MakeMon(hp: 100) });
            var state = new BattleState(p1, p2);
            var random = new FixedRandomSource();
            var typeChart = new StaticTypeChart();

            var result = BattleEngine.ProcessTurn(state, null, null, typeChart, random);

            Assert.AreEqual(2, result.Actions.Count);
            Assert.IsTrue(result.Actions.All(a => a.Kind == BattleActionKind.Skip));
        }
    }
}
