using Atlas.BattleCore;
using Atlas.BattleServer.Battle;
using Atlas.MasterData;
using Microsoft.Extensions.Configuration;

namespace Atlas.BattleServer.Tests
{
    // 配置済みのマスタ(masterdata.bytes)を使い、所持データからBattleCore用の型が組み立てられることの確認。
    public class LoadoutBuilderTests
    {
        private static readonly MemoryDatabase Database = MasterDatabaseFactory.Load(new ConfigurationBuilder().Build());

        private static readonly EffortValues NoEffort = new(0, 0, 0, 0, 0, 0);

        [Fact]
        public void Build_UsesMasterBaseStatsTypesAndMoves()
        {
            // 1031 トリプルドラガ(あく/ドラゴン、種族値 92/105/90/125/90/98)、技 33 ダークバースト・19 ぶつかり。
            var loadout = LoadoutBuilder.Build(Database, new OwnedPachimon(1031, NoEffort, [33, 19]));

            Assert.Equal(1031, loadout.PachimonId);
            Assert.Equal(
                new ParticipantStats(
                    Level: 50, Hp: 152, Atk: 110, Def: 95, SpAtk: 130, SpDef: 95, Speed: 103,
                    PrimaryType: ElementType.Dark, SecondaryType: ElementType.Dragon),
                loadout.Stats);
            Assert.Equal(["33", "19"], loadout.Moves.Select(m => m.MoveId));
            Assert.Equal(new MoveData(ElementType.Dark, MoveCategory.Special, 90, 100, 15), loadout.Moves[0].Data);
            Assert.Equal(new MoveData(ElementType.Normal, MoveCategory.Physical, 40, 100, 35), loadout.Moves[1].Data);
        }

        [Fact]
        public void Build_AppliesEffortValues()
        {
            // floor((2*98 + floor(252/4)) * 50 / 100) + 5 = floor(259 * 0.5) + 5 = 134
            var loadout = LoadoutBuilder.Build(Database, new OwnedPachimon(1031, NoEffort with { Speed = 252, Hp = 4 }, [33]));

            Assert.Equal(134, loadout.Stats.Speed);
            // floor((2*92 + 1) * 50 / 100) + 50 + 10 = 92 + 60 = 152(EV4は切り捨てで変わらない)
            Assert.Equal(152, loadout.Stats.Hp);
        }

        [Fact]
        public void Build_SingleTypePachimon_HasNoSecondaryType()
        {
            // 1033 シフォンテール(フェアリー単タイプ)
            var loadout = LoadoutBuilder.Build(Database, new OwnedPachimon(1033, NoEffort, [35]));

            Assert.Equal(ElementType.Fairy, loadout.Stats.PrimaryType);
            Assert.Null(loadout.Stats.SecondaryType);
        }

        [Fact]
        public void Build_UnknownMasterIds_Throw()
        {
            Assert.Throws<InvalidOperationException>(() => LoadoutBuilder.Build(Database, new OwnedPachimon(99999, NoEffort, [19])));
            Assert.Throws<InvalidOperationException>(() => LoadoutBuilder.Build(Database, new OwnedPachimon(1031, NoEffort, [99999])));
        }

        [Fact]
        public void BuildTypeChart_UsesMasterEffectiveness()
        {
            var chart = LoadoutBuilder.BuildTypeChart(Database);

            Assert.Equal(EffectivenessResult.SuperEffective, chart.GetEffectiveness(ElementType.Fire, ElementType.Grass));
            Assert.Equal(EffectivenessResult.NotVeryEffective, chart.GetEffectiveness(ElementType.Fire, ElementType.Water));
            Assert.Equal(EffectivenessResult.Immune, chart.GetEffectiveness(ElementType.Normal, ElementType.Ghost));
            Assert.Equal(EffectivenessResult.Normal, chart.GetEffectiveness(ElementType.Normal, ElementType.Normal));
        }
    }
}
