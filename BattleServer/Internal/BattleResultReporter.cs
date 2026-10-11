using System.Text.Json;
using Atlas.BattleServer.Battle;
using Atlas.Internal.V1;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Atlas.BattleServer.Internal
{
    // 対戦結果。Player1Id/Player2IdはBattleServer側の順番(先にJoinAsyncした側がPlayer1)で、Rust側の
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

    // battle_turnsの1行に対応する。ActionData/ResultDataはJSONにしてRust側のJSON列にそのまま入る。
    public sealed record BattleTurnRecord(int TurnNumber, string PlayerId, TurnActionData ActionData, TurnResultData ResultData);

    public sealed record TurnActionData(string Type, string? MoveId, int? PartySlot);

    // 内部記録用のためHPはクライアントへ送らない生値(DamageDealt/TargetRemainingHp)も含める。
    public sealed record TurnResultData(
        bool Hit, bool Critical, string Effectiveness, int DamageDealt, int TargetRemainingHp, bool TargetFainted, int? NewActiveIndex);

    // 対戦結果をRust APIサーバーの内部API(BattleInternalService/ReportResult)へ報告する。
    // サービス間シークレットはメタデータx-internal-secretで送り、共有値は両サーバーとも
    // 環境変数INTERNAL_API_SECRETで配布する。
    public sealed class BattleResultReporter
    {
        public const string BaseUrlConfigKey = "API_SERVER_URL";
        public const string SecretConfigKey = "INTERNAL_API_SECRET";
        public const string SecretHeaderName = "x-internal-secret";

        // Rust側(Server/src/main.rs)のDEFAULT_SERVER_ADDRに合わせたローカル開発用のデフォルト値。
        public const string DefaultBaseUrl = "http://127.0.0.1:3000";

        // 行動・結果のJSONのキー(camelCase)。Rust側は中身を解釈せずに保存するので、記録の形はここで決まる
        private static readonly JsonSerializerOptions TurnJsonOptions = new(JsonSerializerDefaults.Web);

        // 一時的な失敗として再送するステータス(通信できない・サーバー内部のエラー)。
        private static readonly HashSet<StatusCode> TransientStatusCodes =
        [
            StatusCode.Unavailable, StatusCode.DeadlineExceeded, StatusCode.Internal, StatusCode.Unknown,
            StatusCode.ResourceExhausted, StatusCode.Aborted,
        ];

        private readonly BattleInternalService.BattleInternalServiceClient client;
        private readonly string? secret;
        private readonly TimeSpan[] retryDelays;
        private readonly ILogger<BattleResultReporter> logger;

        public BattleResultReporter(
            BattleInternalService.BattleInternalServiceClient client,
            IConfiguration configuration,
            IOptions<BattleTimingOptions> timingOptions,
            ILogger<BattleResultReporter> logger)
        {
            this.client = client;
            secret = configuration[SecretConfigKey];
            retryDelays = timingOptions.Value.ResultReportRetryDelays;
            this.logger = logger;
        }

        // 失敗してもバトル自体(クライアントへのOnBattleEnd)には影響させず、ログに残すのみ。
        // 通信できない・サーバー内部のエラーは一時的な失敗としてResultReportRetryDelaysの間隔で再送する。
        // ALREADY_EXISTS(記録済み)は再送で先に届いていた等の二重報告なので成功扱い、それ以外の失敗は
        // 再送しても結果が変わらないため即座に諦める。
        // TODO: 再送し切っても失敗した場合の永続化は未対応(プロセス内のメモリにしか無いため、
        // その対戦はRust側で終了扱いにならない)。
        public async Task ReportAsync(BattleResultRequest request)
        {
            if (string.IsNullOrEmpty(secret))
            {
                logger.LogWarning(
                    "{SecretKey} is not set; skipped reporting battle result. matchId={MatchId} winnerId={WinnerId}",
                    SecretConfigKey, request.MatchId, request.WinnerId);
                return;
            }

            var message = ToMessage(request);
            for (int attempt = 0; ; attempt++)
            {
                if (await TrySendAsync(message, attempt))
                {
                    return;
                }

                if (attempt >= retryDelays.Length)
                {
                    logger.LogError("Gave up reporting battle result. matchId={MatchId}", request.MatchId);
                    return;
                }

                await Task.Delay(retryDelays[attempt]);
            }
        }

        // 戻り値: true=完了(成功または再送不要な失敗)、false=再送すべき一時的な失敗。
        private async Task<bool> TrySendAsync(ReportResultRequest message, int attempt)
        {
            try
            {
                await client.ReportResultAsync(message, new Metadata { { SecretHeaderName, secret! } });
                logger.LogInformation("Reported battle result. matchId={MatchId}", message.MatchId);
                return true;
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.AlreadyExists)
            {
                logger.LogInformation("Battle result was already recorded. matchId={MatchId}", message.MatchId);
                return true;
            }
            catch (RpcException e) when (TransientStatusCodes.Contains(e.StatusCode))
            {
                logger.LogWarning(
                    "Failed to report battle result. matchId={MatchId} status={StatusCode} attempt={Attempt}",
                    message.MatchId, e.StatusCode, attempt + 1);
                return false;
            }
            catch (RpcException e)
            {
                logger.LogError(
                    "Battle result was rejected. matchId={MatchId} status={StatusCode} detail={Detail}",
                    message.MatchId, e.StatusCode, e.Status.Detail);
                return true;
            }
            catch (Exception e)
            {
                // 呼び出し元は完了を待たない(fire-and-forget)ため、想定外の例外もここで止めてログに残す。
                logger.LogError(e, "Failed to report battle result. matchId={MatchId}", message.MatchId);
                return true;
            }
        }

        private static ReportResultRequest ToMessage(BattleResultRequest request)
        {
            var message = new ReportResultRequest
            {
                MatchId = request.MatchId,
                WinnerId = request.WinnerId,
                Player1Id = request.Player1Id,
                Player2Id = request.Player2Id,
                Player1SelectedPachimon = { request.Player1SelectedPachimon },
                Player2SelectedPachimon = { request.Player2SelectedPachimon },
            };
            foreach (var turn in request.Turns)
            {
                message.Turns.Add(new BattleTurn
                {
                    TurnNumber = turn.TurnNumber,
                    PlayerId = turn.PlayerId,
                    ActionDataJson = JsonSerializer.Serialize(turn.ActionData, TurnJsonOptions),
                    ResultDataJson = JsonSerializer.Serialize(turn.ResultData, TurnJsonOptions),
                });
            }
            return message;
        }
    }
}
