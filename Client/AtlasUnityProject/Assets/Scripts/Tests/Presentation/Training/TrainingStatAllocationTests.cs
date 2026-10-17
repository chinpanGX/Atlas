using Atlas.Domain;
using Atlas.Presentation.Training;
using NUnit.Framework;

namespace Atlas.Tests.Presentation.Training
{
    public sealed class TrainingStatAllocationTests
    {
        private const int Hp = 0;
        private const int Atk = 1;
        private const int Spatk = 3;
        private const int Speed = 5;

        [Test]
        public void Increase_FromZero_CountsOneTicketPerPoint()
        {
            var allocation = new TrainingStatAllocation(StatPoints.Zero);

            for (var i = 0; i < 10; i++)
            {
                allocation.Increase(Hp);
            }

            Assert.AreEqual(10, allocation.GetAdjusted(Hp));
            Assert.AreEqual(54, allocation.RemainingPoints);
            Assert.AreEqual(10, allocation.RequiredTickets);
            Assert.IsTrue(allocation.IsDirty);
        }

        [Test]
        public void MovePoints_CountsOnlyIncreasedPoints()
        {
            var allocation = new TrainingStatAllocation(new StatPoints(0, 0, 0, 32, 0, 32));

            for (var i = 0; i < 10; i++)
            {
                allocation.Decrease(Spatk);
                allocation.Increase(Hp);
            }

            Assert.AreEqual(22, allocation.GetAdjusted(Spatk));
            Assert.AreEqual(10, allocation.GetAdjusted(Hp));
            Assert.AreEqual(10, allocation.RequiredTickets);
            Assert.AreEqual(0, allocation.RemainingPoints);
        }

        [Test]
        public void DecreaseOnly_RequiresNoTickets()
        {
            var allocation = new TrainingStatAllocation(new StatPoints(0, 10, 0, 0, 0, 0));

            allocation.Decrease(Atk);

            Assert.AreEqual(0, allocation.RequiredTickets);
            Assert.IsTrue(allocation.IsDirty);
        }

        [Test]
        public void IncreaseThenDecreaseSameStat_IsNotDirty()
        {
            var allocation = new TrainingStatAllocation(new StatPoints(0, 10, 0, 0, 0, 0));

            allocation.Increase(Atk);
            allocation.Decrease(Atk);

            Assert.AreEqual(0, allocation.RequiredTickets);
            Assert.IsFalse(allocation.IsDirty);
        }

        [Test]
        public void Increase_WhenNoRemainingPoints_DoesNothing()
        {
            var allocation = new TrainingStatAllocation(new StatPoints(0, 0, 0, 0, 0, 64));

            Assert.IsFalse(allocation.CanIncrease(Hp));
            allocation.Increase(Hp);

            Assert.AreEqual(0, allocation.GetAdjusted(Hp));
            Assert.IsFalse(allocation.IsDirty);
        }

        [Test]
        public void Decrease_AtZero_DoesNothing()
        {
            var allocation = new TrainingStatAllocation(StatPoints.Zero);

            Assert.IsFalse(allocation.CanDecrease(Speed));
            allocation.Decrease(Speed);

            Assert.AreEqual(0, allocation.GetAdjusted(Speed));
        }

        [Test]
        public void Revert_RestoresCommittedPoints()
        {
            var allocation = new TrainingStatAllocation(new StatPoints(1, 2, 3, 4, 5, 6));

            allocation.Increase(Hp);
            allocation.Decrease(Speed);
            allocation.Revert();

            Assert.IsFalse(allocation.IsDirty);
            Assert.AreEqual(1, allocation.GetAdjusted(Hp));
            Assert.AreEqual(6, allocation.GetAdjusted(Speed));
        }

        [Test]
        public void ToAdjustedStatPoints_KeepsStatOrder()
        {
            var allocation = new TrainingStatAllocation(new StatPoints(1, 2, 3, 4, 5, 6));

            allocation.Increase(Atk);
            var points = allocation.ToAdjustedStatPoints();

            Assert.AreEqual(1, points.Hp);
            Assert.AreEqual(3, points.Atk);
            Assert.AreEqual(3, points.Def);
            Assert.AreEqual(4, points.Spatk);
            Assert.AreEqual(5, points.Spdef);
            Assert.AreEqual(6, points.Speed);
        }
    }
}
