using System.Net;
using System.Net.Http.Json;
using Atlas.BattleContracts;
using Atlas.BattleCore;
using Atlas.BattleServer.Battle;
using Atlas.MasterData;

namespace Atlas.BattleServer.Internal
{
    // POST /internal/battle/loadouts のリクエスト/レスポンス(Server/src/api/internal.rsと同じ形)。
    // JsonContent.Create/ReadFromJsonAsyncのデフォルト(JsonSerializerDefaults.Web)でcamelCase・
    // 大文字小文字を区別しない読み取りになる。
    public sealed record LoadoutRequest(string PlayerId, IReadOnlyList<string> PlayerPachimonIds);

    public sealed record LoadoutResponse(IReadOnlyList<LoadoutResponseItem> Pachimon);

    public sealed record LoadoutResponseItem(
        string PlayerPachimonId, int PachimonId, EffortValues EffortValues, IReadOnlyList<int> MoveIds);

    // POST /internal/battle/party のリクエスト/レスポンス(Server/src/api/internal.rsと同じ形)。
    public sealed record PartyRequest(string PlayerId);

    public sealed record PartyResponse(IReadOnlyList<PartyPachimon> Pachimon);

    // 選出候補(パーティ編成)と、選出個体の所持データ(どのパチモンか・努力値・覚えている技)をRustの内部APIから
    // 取得し、後者はマスタ(MemoryDatabase)と組み合わせてBattleCore用の型にする。所持チェックもRust側で行う
    // (他プレイヤーの個体・存在しないIDは404)。
    public sealed class ApiParticipantDataSource : IParticipantDataSource
    {
        private const string LoadoutsPath = "/internal/battle/loadouts";
        private const string PartyPath = "/internal/battle/party";

        // シングルトン(BattleCoordinator)から使うため、HttpClientは都度IHttpClientFactoryから取得する。
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MemoryDatabase _database;
        private readonly string _secret;

        public ITypeChart TypeChart { get; }

        public ApiParticipantDataSource(IHttpClientFactory httpClientFactory, MemoryDatabase database, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _database = database;
            // 結果報告(BattleResultReporter)は未設定なら送信を諦めるだけだが、こちらは無いと対戦を
            // 始められないため、未設定なら起動時に失敗させる(Program.csで起動時に解決している)。
            _secret = configuration[BattleResultReporter.SecretConfigKey] is { Length: > 0 } secret
                ? secret
                : throw new InvalidOperationException($"{BattleResultReporter.SecretConfigKey} is not set");
            TypeChart = LoadoutBuilder.BuildTypeChart(database);
        }

        public async Task<IReadOnlyList<PartyPachimon>> GetPartyAsync(string playerId)
        {
            using var response = await PostAsync(PartyPath, new PartyRequest(playerId));
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<PartyResponse>()
                ?? throw new InvalidOperationException("empty response from " + PartyPath);
            return body.Pachimon;
        }

        public async Task<IReadOnlyList<ParticipantLoadout>?> ResolveAsync(string playerId, IReadOnlyList<string> playerPachimonIds)
        {
            using var response = await PostAsync(LoadoutsPath, new LoadoutRequest(playerId, playerPachimonIds));
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<LoadoutResponse>()
                ?? throw new InvalidOperationException("empty response from " + LoadoutsPath);
            if (body.Pachimon.Count != playerPachimonIds.Count)
            {
                throw new InvalidOperationException(
                    $"{LoadoutsPath} returned {body.Pachimon.Count} pachimon for {playerPachimonIds.Count} ids");
            }

            return body.Pachimon
                .Select(p => LoadoutBuilder.Build(_database, new OwnedPachimon(p.PachimonId, p.EffortValues, p.MoveIds)))
                .ToList();
        }

        private async Task<HttpResponseMessage> PostAsync<T>(string path, T body)
        {
            var client = _httpClientFactory.CreateClient(BattleResultReporter.HttpClientName);
            using var message = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body),
            };
            message.Headers.Add(BattleResultReporter.SecretHeaderName, _secret);
            return await client.SendAsync(message);
        }
    }
}
