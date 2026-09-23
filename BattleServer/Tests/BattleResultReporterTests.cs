using System.Net;

namespace Atlas.BattleServer.Tests
{
    // /internal/battle/resultへの報告の再送方針のテスト。投了で決着させて報告を発生させる
    // (BattleServerTestHostは再送間隔10ms・再送1回に設定している)。
    public class BattleResultReporterTests
    {
        [Fact]
        public async Task ServerError_IsRetried()
        {
            await using var host = new BattleServerTestHost();
            host.ApiServer.EnqueueResponses(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

            await ForfeitAsync(host);

            await host.ApiServer.WaitForCountAsync(2);
            var bodies = host.ApiServer.Received.Select(r => r.Body).Distinct();
            Assert.Single(bodies);
        }

        [Fact]
        public async Task RetriesAreBounded()
        {
            await using var host = new BattleServerTestHost();
            host.ApiServer.EnqueueResponses(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);

            await ForfeitAsync(host);

            await host.ApiServer.WaitForCountAsync(2);
            await Task.Delay(300);
            Assert.Equal(2, host.ApiServer.Received.Count);
        }

        [Theory]
        [InlineData(HttpStatusCode.Conflict)]    // 記録済み(二重報告)なので成功扱い
        [InlineData(HttpStatusCode.BadRequest)]  // 再送しても結果が変わらない
        [InlineData(HttpStatusCode.Unauthorized)]
        public async Task ClientError_IsNotRetried(HttpStatusCode status)
        {
            await using var host = new BattleServerTestHost();
            host.ApiServer.EnqueueResponses(status, HttpStatusCode.OK);

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
