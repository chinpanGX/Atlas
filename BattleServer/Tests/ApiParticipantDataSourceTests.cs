using System.Net;
using System.Text;
using System.Text.Json;
using Atlas.BattleServer.Battle;
using Atlas.BattleServer.Internal;
using Atlas.MasterData;
using Microsoft.Extensions.Configuration;

namespace Atlas.BattleServer.Tests
{
    // Rustの/internal/battle/loadoutsへの問い合わせと、レスポンスの扱い(404=所持していない、それ以外の失敗=例外)の確認。
    public class ApiParticipantDataSourceTests
    {
        private const string Secret = "test-internal-api-secret";

        private static readonly MemoryDatabase Database = MasterDatabaseFactory.Load(new ConfigurationBuilder().Build());

        [Fact]
        public async Task ResolveAsync_SendsRequestAndBuildsLoadoutsInOrder()
        {
            var handler = new StubHandler(HttpStatusCode.OK, """
                {"pachimon": [
                  {"playerPachimonId": "ppB", "pachimonId": 1033, "effortValues": {"hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 0}, "moveIds": [35, 19]},
                  {"playerPachimonId": "ppA", "pachimonId": 1031, "effortValues": {"hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 252}, "moveIds": [33]}
                ]}
                """);
            var dataSource = Create(handler);

            var loadouts = await dataSource.ResolveAsync("player-1", ["ppB", "ppA"]);

            Assert.NotNull(loadouts);
            Assert.Equal([1033, 1031], loadouts.Select(l => l.PachimonId));
            Assert.Equal(["35", "19"], loadouts[0].Moves.Select(m => m.MoveId));
            Assert.Equal(134, loadouts[1].Stats.Speed);

            Assert.Equal("/internal/battle/loadouts", handler.Path);
            Assert.Equal(Secret, handler.Secret);
            using var request = JsonDocument.Parse(handler.Body!);
            Assert.Equal("player-1", request.RootElement.GetProperty("playerId").GetString());
            Assert.Equal(["ppB", "ppA"], request.RootElement.GetProperty("playerPachimonIds").EnumerateArray().Select(e => e.GetString()));
        }

        [Fact]
        public async Task ResolveAsync_NotOwned_ReturnsNull()
        {
            var dataSource = Create(new StubHandler(HttpStatusCode.NotFound, ""));

            Assert.Null(await dataSource.ResolveAsync("player-1", ["someone-elses"]));
        }

        [Fact]
        public async Task ResolveAsync_ServerError_Throws()
        {
            var dataSource = Create(new StubHandler(HttpStatusCode.InternalServerError, ""));

            await Assert.ThrowsAsync<HttpRequestException>(() => dataSource.ResolveAsync("player-1", ["ppA"]));
        }

        [Fact]
        public void Constructor_MissingSecret_Throws()
        {
            var configuration = new ConfigurationBuilder().Build();

            Assert.Throws<InvalidOperationException>(
                () => new ApiParticipantDataSource(new StubHttpClientFactory(new StubHandler(HttpStatusCode.OK, "")), Database, configuration));
        }

        private static ApiParticipantDataSource Create(StubHandler handler)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [BattleResultReporter.SecretConfigKey] = Secret })
                .Build();
            return new ApiParticipantDataSource(new StubHttpClientFactory(handler), Database, configuration);
        }

        private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) =>
                new(handler, disposeHandler: false) { BaseAddress = new Uri("http://api.test") };
        }

        private sealed class StubHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
        {
            public string? Path { get; private set; }
            public string? Secret { get; private set; }
            public string? Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Path = request.RequestUri!.AbsolutePath;
                Secret = request.Headers.TryGetValues(BattleResultReporter.SecretHeaderName, out var values) ? values.Single() : null;
                Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}
