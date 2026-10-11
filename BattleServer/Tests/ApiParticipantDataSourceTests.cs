using Atlas.BattleServer.Battle;
using Atlas.BattleServer.Internal;
using Atlas.Internal.V1;
using Atlas.MasterData;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using EffortValues = Atlas.Internal.V1.EffortValues;

namespace Atlas.BattleServer.Tests
{
    // Rustの内部API(BattleInternalService)への問い合わせと、レスポンスの扱い(NOT_FOUND=所持していない、それ以外の失敗=例外)の確認。
    public class ApiParticipantDataSourceTests
    {
        private const string Secret = "test-internal-api-secret";

        private static readonly MemoryDatabase Database = MasterDatabaseFactory.Load(new ConfigurationBuilder().Build());

        [Fact]
        public async Task ResolveAsync_SendsRequestAndBuildsLoadoutsInOrder()
        {
            var api = new FakeInternalApi
            {
                LoadoutsResponse = new GetLoadoutsResponse
                {
                    Loadouts =
                    {
                        new Loadout
                        {
                            PlayerPachimonId = "ppB", PachimonId = 1033, EffortValues = new EffortValues(), MoveIds = { 35, 19 },
                        },
                        new Loadout
                        {
                            PlayerPachimonId = "ppA", PachimonId = 1031, EffortValues = new EffortValues { Speed = 252 }, MoveIds = { 33 },
                        },
                    },
                },
            };
            var dataSource = Create(api);

            var loadouts = await dataSource.ResolveAsync("player-1", ["ppB", "ppA"]);

            Assert.NotNull(loadouts);
            Assert.Equal([1033, 1031], loadouts.Select(l => l.PachimonId));
            Assert.Equal(["35", "19"], loadouts[0].Moves.Select(m => m.MoveId));
            Assert.Equal(134, loadouts[1].Stats.Speed);

            Assert.Equal(Secret, api.LastSecret);
            Assert.Equal("player-1", api.LastLoadoutsRequest!.PlayerId);
            Assert.Equal(["ppB", "ppA"], api.LastLoadoutsRequest.PlayerPachimonIds);
        }

        [Fact]
        public async Task GetPartyAsync_SendsRequestAndReturnsPartyInOrder()
        {
            var api = new FakeInternalApi
            {
                PartyResponse = new GetPartyResponse
                {
                    Pachimon =
                    {
                        new PartyPachimon { PlayerPachimonId = "ppB", PachimonId = 1033 },
                        new PartyPachimon { PlayerPachimonId = "ppA", PachimonId = 1031 },
                    },
                },
            };
            var dataSource = Create(api);

            var party = await dataSource.GetPartyAsync("player-1");

            Assert.Equal(["ppB", "ppA"], party.Select(p => p.PlayerPachimonId));
            Assert.Equal([1033, 1031], party.Select(p => p.PachimonId));
            Assert.Equal(Secret, api.LastSecret);
            Assert.Equal("player-1", api.LastPartyRequest!.PlayerId);
        }

        [Fact]
        public async Task ResolveAsync_NotOwned_ReturnsNull()
        {
            var dataSource = Create(new FakeInternalApi { LoadoutsStatus = StatusCode.NotFound });

            Assert.Null(await dataSource.ResolveAsync("player-1", ["someone-elses"]));
        }

        [Fact]
        public async Task ResolveAsync_ServerError_Throws()
        {
            var dataSource = Create(new FakeInternalApi { LoadoutsStatus = StatusCode.Internal });

            var e = await Assert.ThrowsAsync<RpcException>(() => dataSource.ResolveAsync("player-1", ["ppA"]));
            Assert.Equal(StatusCode.Internal, e.StatusCode);
        }

        [Fact]
        public void Constructor_MissingSecret_Throws()
        {
            var configuration = new ConfigurationBuilder().Build();

            Assert.Throws<InvalidOperationException>(
                () => new ApiParticipantDataSource(new FakeInternalApi(), Database, configuration));
        }

        private static ApiParticipantDataSource Create(FakeInternalApi api)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [BattleResultReporter.SecretConfigKey] = Secret })
                .Build();
            return new ApiParticipantDataSource(api, Database, configuration);
        }
    }
}
