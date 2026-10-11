using System;
using Atlas.Api.V1;
using Cysharp.Threading.Tasks;
using VolTech.Client;
using VolTech.V1;

namespace Atlas.Infrastructure.Mock
{
    // APIサーバーの代わりに、通信の共通処理(ApiCore)と生成したラッパーから呼ばれる偽のサーバー。
    // 状態(サインアップ済みか)を持ち、本物と同じ型のレスポンスとエラーを返す。
    // FakeCallInvokerごと差し替えるので、ラッパー・ApiCore・反映処理は本物と同じに動く。
    public sealed class FakeServer
    {
        private const string RegisterMethod = "/voltech.v1.DeviceService/Register";
        private const string IssueTokenMethod = "/voltech.v1.DeviceService/IssueToken";
        private const string Nickname = "プレイヤー";

        private readonly string deviceId = Guid.NewGuid().ToString("N");
        private readonly string playerId = Guid.NewGuid().ToString("N");
        private bool signedUp;

        public FakeCallInvoker CreateCallInvoker()
        {
            return new FakeCallInvoker()
                .Register<RegisterRequest, RegisterResponse>(RegisterMethod, (_, _) =>
                    UniTask.FromResult(new RegisterResponse { DeviceId = deviceId, PlayerId = playerId }))
                .Register<IssueTokenRequest, IssueTokenResponse>(IssueTokenMethod, (_, _) =>
                    UniTask.FromResult(new IssueTokenResponse
                    {
                        AccessToken = "fake-access-token",
                        ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow.AddHours(1)),
                    }))
                .Register<SignUpRequest, SignUpResponse>(PlayerServiceMethods.SignUp, (_, _) =>
                {
                    signedUp = true;
                    return UniTask.FromResult(new SignUpResponse { PlayerId = playerId, Nickname = Nickname });
                })
                .Register<SignInRequest, SignInResponse>(PlayerServiceMethods.SignIn, (_, _) =>
                {
                    if (!signedUp)
                    {
                        throw FakeRpcErrors.FailedPrecondition(AtlasErrors.PlayerNotFound);
                    }
                    return UniTask.FromResult(new SignInResponse
                    {
                        PlayerId = playerId,
                        Nickname = Nickname,
                        Snapshot = new PlayerSnapshot(),
                    });
                });
        }
    }
}
