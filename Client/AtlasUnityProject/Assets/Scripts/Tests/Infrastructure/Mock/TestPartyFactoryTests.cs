using System.IO;
using Atlas.MasterData;
using MasterMemory;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Infrastructure.Mock.Tests
{
    public sealed class TestPartyFactoryTests
    {
        private MemoryDatabase database;

        [SetUp]
        public void SetUp()
        {
            var path = Path.Combine(Application.dataPath, "Addressables/MasterData/masterdata.bytes");
            database = MasterDataLoader.Load(File.ReadAllBytes(path));
        }

        [Test]
        public void BuildParty_FromRealMasterData_ReturnsFullyEquippedMembers()
        {
            var party = TestPartyFactory.BuildParty(database, new[] { 1001, 1002, 1003 });

            Assert.AreEqual(3, party.Count);
            foreach (var member in party)
            {
                Assert.Greater(member.State.Stats.Hp, 0, $"pachimonId={member.PachimonId}");
                Assert.AreEqual(member.State.Stats.Hp, member.State.CurrentHp, "満タンHPで開始しているはず");
                Assert.GreaterOrEqual(member.State.Moves.Count, 1, $"pachimonId={member.PachimonId}");
                Assert.AreEqual(member.State.Moves.Count, member.MoveIds.Count);
            }
        }

        [Test]
        public void BuildParty_SecondaryTypeNone_MapsToNull()
        {
            // 1009(ドスグラウド)はsecondary_type=0(None)。
            var party = TestPartyFactory.BuildParty(database, new[] { 1009 });

            Assert.IsNull(party[0].State.Stats.SecondaryType);
        }
    }
}
