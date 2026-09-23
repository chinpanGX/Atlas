using Atlas.BattleBot;

// 使い方(BattleServer/で実行):
//   dotnet run --project BattleBot                      ボット1体。1回対戦したら終了
//   dotnet run --project BattleBot -- --loop            対戦が終わるたびに再びマッチングに並ぶ(Unityの対戦相手を常駐させる)
//   dotnet run --project BattleBot -- --count 2         ボット2体を同時に動かす(ボット同士で対戦し、サーバー全体を通しで確認する)
//   dotnet run --project BattleBot -- --api http://127.0.0.1:3000
// 事前にAPIサーバー(Server/)とBattleServerを起動しておく(DEVELOPMENT.md参照)。

var apiUrl = "http://127.0.0.1:3000";
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
    // 実行のたびに新しいデバイス(=新しいプレイヤー)として登録する。スターター編成がそのままパーティになる。
    using var http = new HttpClient { BaseAddress = new Uri(apiUrl) };
    var api = new ApiClient(http);
    var secretKey = Guid.NewGuid().ToString("N");
    var deviceId = await api.RegisterDeviceAsync(secretKey);
    await api.AuthenticateAsync(deviceId, secretKey);
    await api.SignUpAsync(name);
    var (playerId, selection) = await api.SignInAsync();
    Console.WriteLine($"[{name}] プレイヤー {playerId} を作成しました(選出: {string.Join(",", selection)})");

    do
    {
        await api.JoinQueueAsync();
        Console.WriteLine($"[{name}] 対戦相手を探しています…");
        ApiClient.QueueStatus status;
        try
        {
            while ((status = await api.GetQueueStatusAsync()).Status != "matched")
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (OperationCanceledException)
        {
            using var _ = await http.DeleteAsync("/battle/queue");
            throw;
        }

        try
        {
            await new BotBattle(name).RunAsync(status.BattleServer, status.BattleToken, status.MatchId, selection);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[{name}] 対戦中にエラーが発生しました: {e.Message}");
        }
    } while (loop && !ct.IsCancellationRequested);
}
