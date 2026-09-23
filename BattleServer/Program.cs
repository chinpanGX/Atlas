using Atlas.BattleServer.Auth;
using Atlas.BattleServer.Battle;
using Atlas.BattleServer.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    // WORKAROUND: 開発中はHTTP/2のみを受け付け、非TLSでの接続を許可する
    // (Cysharp公式ChatAppサンプルと同じ設定)。architecture.mdの「サーバーは1台構成
    // (ローカル動作を想定)」に合わせた開発時設定であり、デプロイ方式を決める際に見直す。
    options.ConfigureEndpointDefaults(endpointOptions =>
    {
        endpointOptions.Protocols = HttpProtocols.Http2;
    });
});
builder.Services.AddMagicOnion();

// BATTLE_TOKEN_SECRET等は環境変数(またはappsettings/user-secrets)からIConfiguration経由で読む。
builder.Services.AddOptions<BattleTimingOptions>();
builder.Services.AddSingleton<BattleTokenValidator>();
builder.Services.AddSingleton(_ => MasterDatabaseFactory.Load(builder.Configuration));
builder.Services.AddSingleton<IParticipantDataSource, DummyParticipantDataSource>();
builder.Services.AddSingleton<BattleCoordinator>();
builder.Services.AddSingleton<BattleResultReporter>();
builder.Services.AddHttpClient(BattleResultReporter.HttpClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration[BattleResultReporter.BaseUrlConfigKey] ?? BattleResultReporter.DefaultBaseUrl);
});

var app = builder.Build();

// 共有シークレット未設定なら接続を受け付ける前に起動を失敗させる(Rust側と同じ方針)。
app.Services.GetRequiredService<BattleTokenValidator>();
// マスターデータも同様に、読み込めない(未配置・復号失敗)なら起動を失敗させる。
app.Services.GetRequiredService<Atlas.MasterData.MemoryDatabase>();

app.MapMagicOnionService();

app.Run();

// 自動テスト(WebApplicationFactory<Program>)から参照するため。
public partial class Program;
