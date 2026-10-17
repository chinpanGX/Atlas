using Atlas.BattleServer.Auth;
using Atlas.BattleServer.Battle;
using Atlas.BattleServer.Internal;
using Atlas.Internal.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    // WORKAROUND: 開発中はHTTP/2のみを受け付け、非TLSでの接続を許可する
    // (Cysharp公式ChatAppサンプルと同じ設定)。サーバー1台・ローカルでの動作を想定した開発時の設定で、
    // デプロイ方式を決める際に見直す。
    options.ConfigureEndpointDefaults(endpointOptions =>
    {
        endpointOptions.Protocols = HttpProtocols.Http2;
    });
});
builder.Services.AddMagicOnion(options =>
{
    // クライアントが黙って消えた(電波が切れた等で切断が届かない)ときにも、TCPが切れるのを待たずに切断として扱い、
    // 相手への通知(OnOpponentDisconnected)と再接続の猶予を始める。クライアントは自動で応答する。
    options.EnableStreamingHubHeartbeat = true;
    options.StreamingHubHeartbeatInterval = TimeSpan.FromSeconds(5);
    options.StreamingHubHeartbeatTimeout = TimeSpan.FromSeconds(10);
});

// BATTLE_TOKEN_SECRET等は環境変数(またはappsettings/user-secrets)からIConfiguration経由で読む。
builder.Services.AddOptions<BattleTimingOptions>();
builder.Services.AddSingleton<BattleTokenValidator>();
builder.Services.AddSingleton(_ => MasterDatabaseFactory.Load(builder.Configuration));
builder.Services.AddSingleton<IParticipantDataSource, ApiParticipantDataSource>();
builder.Services.AddSingleton<BattleCoordinator>();
builder.Services.AddSingleton<BattleResultReporter>();
// APIサーバーの内部API(gRPC)。チャンネルは1つを使い回す(HTTP/2の接続を共有する)
builder.Services.AddSingleton(_ => GrpcChannel.ForAddress(
    builder.Configuration[BattleResultReporter.BaseUrlConfigKey] ?? BattleResultReporter.DefaultBaseUrl));
builder.Services.AddSingleton(services =>
    new BattleInternalService.BattleInternalServiceClient(services.GetRequiredService<GrpcChannel>()));

var app = builder.Build();

// 共有シークレット未設定なら接続を受け付ける前に起動を失敗させる(Rust側と同じ方針)。
app.Services.GetRequiredService<BattleTokenValidator>();
// マスターデータも同様に、読み込めない(未配置・復号失敗)なら起動を失敗させる。
app.Services.GetRequiredService<Atlas.MasterData.MemoryDatabase>();
// 選出個体の取得に使うINTERNAL_API_SECRETも同様(未設定だと対戦を始められない)。
app.Services.GetRequiredService<IParticipantDataSource>();

app.MapMagicOnionService();

app.Run();

// 自動テスト(WebApplicationFactory<Program>)から参照するため。
public partial class Program;
