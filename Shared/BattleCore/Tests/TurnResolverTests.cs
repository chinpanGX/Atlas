using NUnit.Framework;

namespace Atlas.BattleCore.Tests
{
    public class TurnResolverTests
    {
        private static ParticipantStats MakeStats(int speed) =>
            new(Level: 50, Hp: 150, Atk: 100, Def: 100, SpAtk: 100, SpDef: 100, Speed: speed,
                PrimaryType: ElementType.Normal, SecondaryType: null);

        private static PachimonState MakePachimon(int speed) =>
            new(MakeStats(speed), new[] { new MoveData(ElementType.Normal, MoveCategory.Physical, 40, 100, 15) });

        private static BattleState MakeState(int p1Speed, int p2Speed) => new(
            new BattleSide(new[] { MakePachimon(p1Speed) }),
            new BattleSide(new[] { MakePachimon(p2Speed) }));

        [Test]
        public void DetermineOrder_SwitchAlwaysBeforeMove_EvenWhenSlower()
        {
            var state = MakeState(p1Speed: 10, p2Speed: 999);
            var p1 = PlayerAction.SwitchTo(0);
            var p2 = PlayerAction.UseMove(0);

            var order = TurnResolver.DetermineOrder(state, p1, p2, new FixedRandomSource());

            Assert.AreEqual(2, order.Count);
            Assert.AreEqual(BattleSideId.Player1, order[0].Side);
            Assert.AreEqual(BattleSideId.Player2, order[1].Side);
        }

        [Test]
        public void DetermineOrder_MoveVsMove_FasterSideGoesFirst()
        {
            var state = MakeState(p1Speed: 50, p2Speed: 100);
            var p1 = PlayerAction.UseMove(0);
            var p2 = PlayerAction.UseMove(0);

            var order = TurnResolver.DetermineOrder(state, p1, p2, new FixedRandomSource());

            Assert.AreEqual(BattleSideId.Player2, order[0].Side);
            Assert.AreEqual(BattleSideId.Player1, order[1].Side);
        }

        [Test]
        public void DetermineOrder_SpeedTie_UsesRandomSource()
        {
            var state = MakeState(p1Speed: 50, p2Speed: 50);
            var p1 = PlayerAction.UseMove(0);
            var p2 = PlayerAction.UseMove(0);

            var orderP2First = TurnResolver.DetermineOrder(state, p1, p2, new FixedRandomSource(ints: new[] { 1 }));
            Assert.AreEqual(BattleSideId.Player2, orderP2First[0].Side);

            var orderP1First = TurnResolver.DetermineOrder(state, p1, p2, new FixedRandomSource(ints: new[] { 0 }));
            Assert.AreEqual(BattleSideId.Player1, orderP1First[0].Side);
        }

        [Test]
        public void DetermineOrder_OneSideHasNoAction_OnlyOtherSideReturned()
        {
            var state = MakeState(p1Speed: 50, p2Speed: 50);
            var order = TurnResolver.DetermineOrder(state, PlayerAction.UseMove(0), null, new FixedRandomSource());

            Assert.AreEqual(1, order.Count);
            Assert.AreEqual(BattleSideId.Player1, order[0].Side);
        }

        [Test]
        public void RollHit_RandomAtOrBelowAccuracy_Hits()
        {
            var move = new MoveData(ElementType.Normal, MoveCategory.Physical, 40, Accuracy: 90, MaxPp: 15);

            Assert.IsTrue(TurnResolver.RollHit(move, new FixedRandomSource(ints: new[] { 90 })));
            Assert.IsFalse(TurnResolver.RollHit(move, new FixedRandomSource(ints: new[] { 91 })));
        }
    }
}
