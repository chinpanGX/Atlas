using System.Net.Http.Json;

namespace Atlas.BattleServer.Internal
{
    // POST /internal/battle/result のリクエストボディ(docs/design/battle.md「4. 対戦結果記録(内部API)」)。
    // JsonContent.Createのデフォルト(JsonSerializerDefaults.Web)でcamelCaseになる。
    // Player1Id/Player2IdはBattleServer側の順番(先にJoinAsyncした側がPlayer1)で、Rust側の
    // battle_matches.player1_id/player2_idとは一致するとは限らないため、Rust側はIDで突き合わせる。
    // 相手が一度も参加しなかった場合、その側のIDは空文字・選出は空配列になる。
    public sealed record BattleResultRequest(
        string MatchId,
        string WinnerId,
        string Player1Id,
        string Player2Id,
        string[] Player1SelectedPachimon,
        string[] Player2SelectedPachimon,
        IReadOnlyList<BattleTurnRecord> Turns);

    // battle_turnsの1行に対応する。ActionData/ResultDataはJSONカラムにそのまま入る想定。
    public sealed record BattleTurnRecord(int TurnNumber, string PlayerId, TurnActionData ActionData, TurnResultData ResultData);

    public sealed record TurnActionData(string Type, string? MoveId, int? PartySlot);

    // 内部記録用のためHPはクライアントへ送らない生値(DamageDealt/TargetRemainingHp)も含める。
    public sealed record TurnResultData(
        bool Hit, bool Critical, string Effectiveness, int DamageDealt, int TargetRemainingHp, bool TargetFainted, int? NewActiveIndex);

    // 対戦結果をRust APIサーバーの内部APIへ報告する。
    // Rust側(/internal/battle/result)はまだ未実装のため、サービス間シークレットの受け渡し方式は
    // ここで仮決めしている(X-Internal-Secretヘッダー、共有値はINTERNAL_API_SECRET)。
    // Rust側を実装する際はこの方式に合わせるか、どちらかを変更して揃えること。
    public sealed class BattleResultReporter
    {
        public const string BaseUrlConfigKey = "API_SERVER_URL";
        public const string SecretConfigKey = "INTERNAL_API_SECRET";
        public const string SecretHeaderName = "X-Internal-Secret";

        // Rust側(Server/src/main.rs)のDEFAULT_SERVER_ADDRに合わせたローカル開発用のデフォルト値。
        public const string DefaultBaseUrl = "http://127.0.0.1:3000";

        public const string HttpClientName = "ApiServer";

        private const string ResultPath = "/internal/battle/result";

        // シングルトン(BattleCoordinator)から使うため、HttpClientは都度IHttpClientFactoryから取得する。
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string? _secret;
        private readonly ILogger<BattleResultReporter> _logger;

        public BattleResultReporter(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<BattleResultReporter> logger)
        {
            _httpClientFactory = httpClientFactory;
            _secret = configuration[SecretConfigKey];
            _logger = logger;
        }

        // 失敗してもバトル自体(クライアントへのOnBattleEnd)には影響させず、ログに残すのみ。
        // TODO: リトライ・永続化は未対応。報告に失敗した対戦はRust側で終了扱いにならない。
        public async Task ReportAsync(BattleResultRequest request)
        {
            if (string.IsNullOrEmpty(_secret))
            {
                _logger.LogWarning(
                    "{SecretKey} is not set; skipped reporting battle result. matchId={MatchId} winnerId={WinnerId}",
                    SecretConfigKey, request.MatchId, request.WinnerId);
                return;
            }

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, ResultPath)
                {
                    Content = JsonContent.Create(request),
                };
                message.Headers.Add(SecretHeaderName, _secret);

                using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(message);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Failed to report battle result. matchId={MatchId} status={StatusCode}",
                        request.MatchId, (int)response.StatusCode);
                    return;
                }

                _logger.LogInformation("Reported battle result. matchId={MatchId}", request.MatchId);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to report battle result. matchId={MatchId}", request.MatchId);
            }
        }
    }
}
