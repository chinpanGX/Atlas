using Grpc.Core;

namespace Atlas.BattleServer.Tests
{
    // 内部API(BattleInternalService/ReportResult)への報告の再送方針のテスト。投了で決着させて報告を発生させる
    // (BattleServerTestHostは再送間隔10ms・再送1回に設定している)。
    public class BattleResultReporterTests
    {
        [Fact]
        public async Task ServerError_IsRetried()
        {
            await using var host = new BattleServerTestHost();
            host.ApiServer.EnqueueReportStatuses(StatusCode.Internal, StatusCode.OK);

            await ForfeitAsync(host);

            await host.ApiServer.WaitForCountAsync(2);
            var requests = host.ApiServer.Received.Select(r => r.Request).Distinct();
            Assert.Single(requests);
        }

        [Fact]
        public async Task RetriesAreBounded()
        {
            await using var host = new BattleServerTestHost();
            host.ApiServer.EnqueueReportStatuses(StatusCode.Unavailable, StatusCode.Unavailable, StatusCode.Unavailable);

            await ForfeitAsync(host);

            await host.ApiServer.WaitForCountAsync(2);
            await Task.Delay(300);
            Assert.Equal(2, host.ApiServer.Received.Count);
        }

        [Theory]
        [InlineData(StatusCode.AlreadyExists)]    // 記録済み(二重報告)なので成功扱い
        [InlineData(StatusCode.InvalidArgument)]  // 再送しても結果が変わらない
        [InlineData(StatusCode.Unauthenticated)]
        public async Task ClientError_IsNotRetried(StatusCode status)
        {
            await using var host = new BattleServerTestHost();
            host.ApiServer.EnqueueReportStatuses(status, StatusCode.OK);

            await ForfeitAsync(host);

            await host.ApiServer.WaitForCountAsync(1);
            await Task.Delay(300);
            Assert.Single(host.ApiServer.Received);
        }

        private static async Task ForfeitAsync(BattleServerTestHost host)
        {
            var (a, ra, _, _) = await host.StartBattleAsync("m", ["a1"], ["b1"]);
            await a.ForfeitAsync();
            await ra.WaitForAsync(r => r.Ends.Count == 1);
        }
    }
}
