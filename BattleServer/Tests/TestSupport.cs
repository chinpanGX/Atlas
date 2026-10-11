using System.Security.Cryptography;
using System.Text;
using Atlas.BattleServer.Battle;
using Atlas.BattleContracts;
using Atlas.BattleServer.Internal;
using Atlas.Internal.V1;
using Grpc.Net.Client;
using MagicOnion.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.BattleServer.Tests
{
    // BattleServerをインプロセス(TestServer)で起動し、MagicOnionクライアントで接続するためのテスト用ホスト。
    // APIサーバーの内部APIへの結果報告はFakeInternalApiで受け止める。
    public sealed class BattleServerTestHost : IAsyncDisposable
    {
        public const string TokenSecret = "test-battle-token-secret-0123456789abcdef";
        public const string InternalSecret = "test-internal-api-secret";

        private readonly WebApplicationFactory<Program> factory;
        private readonly GrpcChannel channel;
        private readonly List<IBattleHub> hubs = [];

        public FakeInternalApi ApiServer { get; } = new();

        public BattleServerTestHost(Action<BattleTimingOptions>? configureTiming = null)
        {
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("BATTLE_TOKEN_SECRET", TokenSecret);
                builder.UseSetting(BattleResultReporter.SecretConfigKey, InternalSecret);
                builder.ConfigureTestServices(services =>
                {
                    services.Configure<BattleTimingOptions>(options =>
                    {
                        options.ResultReportRetryDelays = [TimeSpan.FromMilliseconds(10)];
                        configureTiming?.Invoke(options);
                    });
                    services.AddSingleton<BattleInternalService.BattleInternalServiceClient>(ApiServer);
                    // Hubの進行だけを検証するため、選出個体の取得(Rustの内部API)は固定値に差し替える。
                    services.AddSingleton<IParticipantDataSource, DummyParticipantDataSource>();
                });
            });

            channel = GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
            {
                HttpHandler = factory.Server.CreateHandler(),
            });
        }

        public async Task<(IBattleHub Hub, TestReceiver Receiver)> ConnectAsync()
        {
            var receiver = new TestReceiver();
            var hub = await StreamingHubClient.ConnectAsync<IBattleHub, IBattleHubReceiver>(channel, receiver);
            hubs.Add(hub);
            return (hub, receiver);
        }

        // 2人とも参加・選出まで済ませ、両者がOnMatchStartを受信した状態にする。
        public async Task<BattlePair> StartBattleAsync(string matchId, string[] selectionA, string[] selectionB)
        {
            var (a, ra) = await ConnectAsync();
            var (b, rb) = await ConnectAsync();
            Assert.Equal(JoinResultStatus.Success, (await a.JoinAsync(Token(matchId, "pA"), matchId)).Status);
            Assert.Equal(JoinResultStatus.Success, (await b.JoinAsync(Token(matchId, "pB"), matchId)).Status);
            await a.SubmitSelectionAsync(selectionA);
            await b.SubmitSelectionAsync(selectionB);
            await ra.WaitForAsync(r => r.Starts.Count == 1);
            await rb.WaitForAsync(r => r.Starts.Count == 1);
            return new BattlePair(a, ra, b, rb);
        }

        // Rust側(jsonwebtoken, Header::default())と同じ形式のHS256 JWTを作る。
        public static string Token(string matchId, string playerId, long expOffsetSeconds = 30, string secret = TokenSecret)
        {
            static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            var header = B64(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"HS256\"}"));
            var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expOffsetSeconds;
            var payload = B64(Encoding.UTF8.GetBytes($"{{\"match_id\":\"{matchId}\",\"player_id\":\"{playerId}\",\"exp\":{exp}}}"));
            var signature = B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{header}.{payload}")));
            return $"{header}.{payload}.{signature}";
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var hub in hubs)
            {
                try
                {
                    await hub.DisposeAsync();
                }
                catch
                {
                    // テスト中に既に切断済みのものは無視する
                }
            }

            channel.Dispose();
            await factory.DisposeAsync();
        }
    }

    public sealed record BattlePair(IBattleHub A, TestReceiver RA, IBattleHub B, TestReceiver RB);

    public sealed class TestReceiver : IBattleHubReceiver
    {
        private readonly object gate = new();

        public List<SelectionStartPayload> SelectionStarts { get; } = [];
        public List<BattleStartPayload> Starts { get; } = [];
        public List<TurnResultPayload> Turns { get; } = [];
        public List<BattleEndPayload> Ends { get; } = [];
        public int OpponentDisconnectedCount { get; private set; }
        public int OpponentReconnectedCount { get; private set; }

        public void OnSelectionStart(SelectionStartPayload payload) { lock (gate) SelectionStarts.Add(payload); }
        public void OnMatchStart(BattleStartPayload payload) { lock (gate) Starts.Add(payload); }
        public void OnTurnResult(TurnResultPayload payload) { lock (gate) Turns.Add(payload); }
        public void OnBattleEnd(BattleEndPayload payload) { lock (gate) Ends.Add(payload); }
        public void OnOpponentDisconnected() { lock (gate) OpponentDisconnectedCount++; }
        public void OnOpponentReconnected() { lock (gate) OpponentReconnectedCount++; }

        public async Task WaitForAsync(Func<TestReceiver, bool> condition, int timeoutSeconds = 10)
        {
            var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < until)
            {
                lock (gate)
                {
                    if (condition(this))
                    {
                        return;
                    }
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("Receiver condition was not met in time.");
        }
    }
}
