using System.Collections.Concurrent;
using Atlas.BattleServer.Internal;
using Atlas.Internal.V1;
using Grpc.Core;

namespace Atlas.BattleServer.Tests
{
    // Rust APIサーバーの内部API(BattleInternalService)の代わり。生成したクライアントのメソッドを上書きし、
    // 受け取ったリクエストと共有シークレットを記録して、決めておいたレスポンス・ステータスを返す。
    public sealed class FakeInternalApi : BattleInternalService.BattleInternalServiceClient
    {
        private readonly ConcurrentQueue<StatusCode> reportStatuses = new();

        // 受け取った結果報告。受け取った順に並ぶ。
        public ConcurrentQueue<ReceivedReport> Received { get; } = new();

        public GetPartyResponse PartyResponse { get; set; } = new();
        public GetLoadoutsResponse LoadoutsResponse { get; set; } = new();
        public StatusCode LoadoutsStatus { get; set; } = StatusCode.OK;

        // 最後に受け取ったパーティ・選出個体の取得のリクエストと共有シークレット。
        public GetPartyRequest? LastPartyRequest { get; private set; }
        public GetLoadoutsRequest? LastLoadoutsRequest { get; private set; }
        public string? LastSecret { get; private set; }

        // 結果報告に返すステータスをFIFOで足す(空ならOK)。
        public void EnqueueReportStatuses(params StatusCode[] statusCodes)
        {
            foreach (var statusCode in statusCodes)
            {
                reportStatuses.Enqueue(statusCode);
            }
        }

        public override AsyncUnaryCall<GetPartyResponse> GetPartyAsync(GetPartyRequest request, CallOptions options)
        {
            LastPartyRequest = request;
            LastSecret = SecretOf(options);
            return Respond(StatusCode.OK, PartyResponse);
        }

        public override AsyncUnaryCall<GetLoadoutsResponse> GetLoadoutsAsync(GetLoadoutsRequest request, CallOptions options)
        {
            LastLoadoutsRequest = request;
            LastSecret = SecretOf(options);
            return Respond(LoadoutsStatus, LoadoutsResponse);
        }

        public override AsyncUnaryCall<ReportResultResponse> ReportResultAsync(ReportResultRequest request, CallOptions options)
        {
            Received.Enqueue(new ReceivedReport(request, SecretOf(options)));
            return Respond(reportStatuses.TryDequeue(out var status) ? status : StatusCode.OK, new ReportResultResponse());
        }

        public async Task WaitForCountAsync(int count, int timeoutSeconds = 10)
        {
            var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (Received.Count < count)
            {
                if (DateTime.UtcNow >= until)
                {
                    throw new TimeoutException($"Expected {count} requests but got {Received.Count}.");
                }

                await Task.Delay(20);
            }
        }

        private static string SecretOf(CallOptions options)
        {
            return options.Headers?.GetValue(BattleResultReporter.SecretHeaderName) ?? "";
        }

        private static AsyncUnaryCall<T> Respond<T>(StatusCode status, T response)
        {
            var task = status == StatusCode.OK
                ? Task.FromResult(response)
                : Task.FromException<T>(new RpcException(new Status(status, "fake")));
            return new AsyncUnaryCall<T>(
                task, Task.FromResult(new Metadata()), () => new Status(status, ""), () => new Metadata(), () => { });
        }
    }

    public sealed record ReceivedReport(ReportResultRequest Request, string Secret);
}
