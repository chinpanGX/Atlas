using Atlas.BattleContracts;
using MagicOnion.Client;

namespace Atlas.BattleServer.Tests
{
    // サーバー・クライアントのハートビート。切断の検知そのもの(応答が途絶えた場合)は、MagicOnionのクライアントが
    // 必ず応答するため結合テストでは作れない。ここでは、生きている接続がハートビートで切られないことを確かめる。
    public class HeartbeatTests
    {
        [Fact]
        public async Task LiveConnection_IsNotDisconnectedByHeartbeats()
        {
            await using var host = new BattleServerTestHost(configureMagicOnion: o =>
            {
                o.EnableStreamingHubHeartbeat = true;
                o.StreamingHubHeartbeatInterval = TimeSpan.FromMilliseconds(100);
                o.StreamingHubHeartbeatTimeout = TimeSpan.FromMilliseconds(300);
            });
            int serverHeartbeats = 0;
            int clientHeartbeatResponses = 0;
            var options = StreamingHubClientOptions.CreateWithDefault()
                .WithClientHeartbeatInterval(TimeSpan.FromMilliseconds(100))
                .WithClientHeartbeatTimeout(TimeSpan.FromMilliseconds(300))
                .WithServerHeartbeatReceived(_ => Interlocked.Increment(ref serverHeartbeats))
                .WithClientHeartbeatResponseReceived(_ => Interlocked.Increment(ref clientHeartbeatResponses));
            var (hub, _) = await host.ConnectAsync(options);
            var disconnected = hub.WaitForDisconnectAsync();

            await Task.Delay(1500);

            Assert.False(disconnected.IsCompleted);
            Assert.True(serverHeartbeats > 3, $"serverHeartbeats={serverHeartbeats}");
            Assert.True(clientHeartbeatResponses > 3, $"clientHeartbeatResponses={clientHeartbeatResponses}");
            Assert.Equal(JoinResultStatus.Success,
                (await hub.JoinAsync(BattleServerTestHost.Token("m", "pA"), "m")).Status);
        }
    }
}
