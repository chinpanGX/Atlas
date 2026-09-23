using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atlas.BattleBot
{
    // ボットが使うAPIサーバー(Rust)のREST呼び出し。Unityクライアントと同じ順番
    // (デバイス登録→認証→サインアップ→サインイン→マッチング)で呼ぶ。
    public sealed class ApiClient(HttpClient http)
    {
        public sealed record QueueStatus(string Status, string MatchId, string BattleServer, string BattleToken);

        public async Task<string> RegisterDeviceAsync(string secretKey)
        {
            var json = await PostAsync("/devices", new { secretKey });
            return json.GetProperty("deviceId").GetString()!;
        }

        public async Task AuthenticateAsync(string deviceId, string secretKey)
        {
            var json = await PostAsync("/devices/authenticate", new { deviceId, secretKey });
            // トークンの有効期限(1時間)内に対戦を終える前提のため、ボットでは再認証しない。
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        }

        public async Task SignUpAsync(string nickname)
        {
            using var response = await http.PostAsJsonAsync("/signup", new { nickname });
            response.EnsureSuccessStatusCode();
        }

        // サインインのplayerDiff(全件)から、パーティの枠番号順に先頭から最大3体のPlayerPachimonIdを返す
        // (Unityクライアントの自動選出と同じ規則)。
        public async Task<(string PlayerId, string[] Selection)> SignInAsync()
        {
            var json = await PostAsync("/sign-in", null);
            var selection = json.GetProperty("playerDiff").GetProperty("partySlots").GetProperty("upserted")
                .EnumerateArray()
                .OrderBy(slot => slot.GetProperty("slot").GetInt32())
                .Take(3)
                .Select(slot => slot.GetProperty("playerPachimonId").GetString()!)
                .ToArray();
            return (json.GetProperty("playerId").GetString()!, selection);
        }

        public async Task JoinQueueAsync()
        {
            using var response = await http.PostAsync("/battle/queue", null);
            response.EnsureSuccessStatusCode();
        }

        public async Task<QueueStatus> GetQueueStatusAsync()
        {
            var json = await http.GetFromJsonAsync<JsonElement>("/battle/queue/status");
            return new QueueStatus(
                json.GetProperty("status").GetString()!,
                json.GetProperty("matchId").GetString()!,
                json.GetProperty("battleServer").GetString()!,
                json.GetProperty("battleToken").GetString()!);
        }

        private async Task<JsonElement> PostAsync(string path, object? body)
        {
            using var response = body is null ? await http.PostAsync(path, null) : await http.PostAsJsonAsync(path, body);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }
}
