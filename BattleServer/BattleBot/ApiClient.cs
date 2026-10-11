using Atlas.Api.V1;
using Grpc.Core;
using Grpc.Net.Client;
using VolTech.V1;

namespace Atlas.BattleBot
{
    // ボットが使うAPIサーバー(Rust)のgRPC呼び出し。Unityクライアントと同じ順番
    // (デバイス登録→トークンの発行→サインアップ→サインイン→マッチング)で呼ぶ。
    // Unityの通信の共通処理(ApiCore)は使えないので、トークンとRequestIDのヘッダーを自分で付ける。
    // 期限前の発行し直しや再送はしない(トークンの有効期限内に対戦を終える前提)。
    public sealed class ApiClient
    {
        private readonly DeviceService.DeviceServiceClient device;
        private readonly PlayerService.PlayerServiceClient player;
        private readonly DebugService.DebugServiceClient debug;
        private readonly MatchmakingService.MatchmakingServiceClient matchmaking;
        private string accessToken = "";

        public ApiClient(GrpcChannel channel)
        {
            device = new DeviceService.DeviceServiceClient(channel);
            player = new PlayerService.PlayerServiceClient(channel);
            debug = new DebugService.DebugServiceClient(channel);
            matchmaking = new MatchmakingService.MatchmakingServiceClient(channel);
        }

        // デバイスを登録してトークンを発行する。
        public async Task RegisterAsync(string secretKey)
        {
            var registered = await device.RegisterAsync(new RegisterRequest { SecretKey = secretKey });
            var issued = await device.IssueTokenAsync(new IssueTokenRequest
            {
                DeviceId = registered.DeviceId,
                SecretKey = secretKey,
            });
            accessToken = issued.AccessToken;
        }

        public async Task SignUpAsync(string nickname)
        {
            await player.SignUpAsync(new SignUpRequest { Nickname = nickname }, Authorized());
        }

        public async Task<string> SignInAsync()
        {
            var response = await player.SignInAsync(new SignInRequest(), Authorized());
            return response.PlayerId;
        }

        // パーティをマスタからランダムに選んだ6体(重複なし、技もランダム)で組み直す開発用API。
        // 選出はBattleServerから届くパーティ(OnSelectionStart)から選ぶため、レスポンスは使わない。
        public async Task RandomizePartyAsync()
        {
            await debug.RandomizePartyAsync(new RandomizePartyRequest(), WithRequestId());
        }

        public async Task JoinQueueAsync()
        {
            await matchmaking.JoinQueueAsync(new JoinQueueRequest(), WithRequestId());
        }

        public async Task LeaveQueueAsync()
        {
            await matchmaking.LeaveQueueAsync(new LeaveQueueRequest(), Authorized());
        }

        public async Task<GetQueueStatusResponse> GetQueueStatusAsync()
        {
            return await matchmaking.GetQueueStatusAsync(new GetQueueStatusRequest(), Authorized());
        }

        private Metadata Authorized()
        {
            return new Metadata { { "authorization", $"Bearer {accessToken}" } };
        }

        // 再送しても一度だけ処理するRPC(冪等性の対象)は、リクエストごとに新しいRequestIDを付ける
        private Metadata WithRequestId()
        {
            var headers = Authorized();
            headers.Add("x-request-id", Guid.NewGuid().ToString("N"));
            return headers;
        }
    }
}
