using Atlas.Api.V1;
using Atlas.BattleBot;
using Grpc.Net.Client;

// 使い方(BattleServer/で実行):
//   dotnet run --project BattleBot                      ボット1体。1回対戦したら終了
//   dotnet run --project BattleBot -- --loop            対戦が終わるたびに再びマッチングに並ぶ(Unityの対戦相手を常駐させる)
//   dotnet run --project BattleBot -- --count 2         ボット2体を同時に動かす(ボット同士で対戦し、サーバー全体を通しで確認する)
//   dotnet run --project BattleBot -- --api http://127.0.0.1:50051   APIサーバー(gRPC)の接続先
// 事前にAPIサーバー(Server/)とBattleServerを起動しておく。

var apiUrl = "http://127.0.0.1:50051";
var count = 1;
var loop = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--api": apiUrl = args[++i]; break;
        case "--count": count = int.Parse(args[++i]); break;
        case "--loop": loop = true; break;
        default:
            Console.Error.WriteLine($"不明な引数: {args[i]}");
            return 1;
    }
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var bots = Enumerable.Range(1, count).Select(n => RunBotAsync($"Bot{n}", cancellation.Token));
try
{
    await Task.WhenAll(bots);
}
catch (OperationCanceledException)
{
}

return 0;

async Task RunBotAsync(string name, CancellationToken ct)
{
    // 実行のたびに新しいデバイス(=新しいプレイヤー)として登録し、パーティをマスタからランダムに組み直す。
    using var channel = GrpcChannel.ForAddress(apiUrl);
    var api = new ApiClient(channel);
    await api.RegisterAsync(Guid.NewGuid().ToString("N"));
    await api.SignUpAsync(name);
    var playerId = await api.SignInAsync();
    await api.RandomizePartyAsync();
    Console.WriteLine($"[{name}] プレイヤー {playerId} を作成しました");

    do
    {
        await api.JoinQueueAsync();
        Console.WriteLine($"[{name}] 対戦相手を探しています…");
        GetQueueStatusResponse status;
        try
        {
            while ((status = await api.GetQueueStatusAsync()).Status != QueueStatus.Matched)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (OperationCanceledException)
        {
            await api.LeaveQueueAsync();
            throw;
        }

        try
        {
            await new BotBattle(name).RunAsync(status.BattleServer, status.BattleToken, status.MatchId);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[{name}] 対戦中にエラーが発生しました: {e.Message}");
        }
    } while (loop && !ct.IsCancellationRequested);
}
