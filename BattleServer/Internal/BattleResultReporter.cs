using System.Net;
using System.Net.Http.Json;
using Atlas.BattleServer.Battle;
using Microsoft.Extensions.Options;

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

    // 対戦結果をRust APIサーバーの内部API(Server/src/api/internal.rs)へ報告する。
    // サービス間シークレットはX-Internal-Secretヘッダーで送り、共有値は両サーバーとも
    // 環境変数INTERNAL_API_SECRETで配布する。
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
        private readonly TimeSpan[] _retryDelays;
        private readonly ILogger<BattleResultReporter> _logger;

        public BattleResultReporter(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IOptions<BattleTimingOptions> timingOptions,
            ILogger<BattleResultReporter> logger)
        {
            _httpClientFactory = httpClientFactory;
            _secret = configuration[SecretConfigKey];
            _retryDelays = timingOptions.Value.ResultReportRetryDelays;
            _logger = logger;
        }

        // 失敗してもバトル自体(クライアントへのOnBattleEnd)には影響させず、ログに残すのみ。
        // 通信エラー・5xxは一時的な失敗としてResultReportRetryDelaysの間隔で再送する。
        // 409(記録済み)は再送で先に届いていた等の二重報告なので成功扱い、それ以外の4xxは
        // 再送しても結果が変わらないため即座に諦める。
        // TODO: 再送し切っても失敗した場合の永続化は未対応(プロセス内のメモリにしか無いため、
        // その対戦はRust側で終了扱いにならない)。
        public async Task ReportAsync(BattleResultRequest request)
        {
            if (string.IsNullOrEmpty(_secret))
            {
                _logger.LogWarning(
                    "{SecretKey} is not set; skipped reporting battle result. matchId={MatchId} winnerId={WinnerId}",
                    SecretConfigKey, request.MatchId, request.WinnerId);
                return;
            }

            for (int attempt = 0; ; attempt++)
            {
                if (await TrySendAsync(request, attempt))
                {
                    return;
                }

                if (attempt >= _retryDelays.Length)
                {
                    _logger.LogError("Gave up reporting battle result. matchId={MatchId}", request.MatchId);
                    return;
                }

                await Task.Delay(_retryDelays[attempt]);
            }
        }

        // 戻り値: true=完了(成功または再送不要な失敗)、false=再送すべき一時的な失敗。
        private async Task<bool> TrySendAsync(BattleResultRequest request, int attempt)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, ResultPath)
                {
                    Content = JsonContent.Create(request),
                };
                message.Headers.Add(SecretHeaderName, _secret);

                using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(message);
                int status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Reported battle result. matchId={MatchId}", request.MatchId);
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    _logger.LogInformation("Battle result was already recorded. matchId={MatchId}", request.MatchId);
                    return true;
                }

                if (status is >= 400 and < 500)
                {
                    _logger.LogError(
                        "Battle result was rejected. matchId={MatchId} status={StatusCode} body={Body}",
                        request.MatchId, status, await response.Content.ReadAsStringAsync());
                    return true;
                }

                _logger.LogWarning(
                    "Failed to report battle result. matchId={MatchId} status={StatusCode} attempt={Attempt}",
                    request.MatchId, status, attempt + 1);
                return false;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(e, "Failed to report battle result. matchId={MatchId} attempt={Attempt}", request.MatchId, attempt + 1);
                return false;
            }
            catch (Exception e)
            {
                // 呼び出し元は完了を待たない(fire-and-forget)ため、想定外の例外もここで止めてログに残す。
                _logger.LogError(e, "Failed to report battle result. matchId={MatchId}", request.MatchId);
                return true;
            }
        }
    }
}
