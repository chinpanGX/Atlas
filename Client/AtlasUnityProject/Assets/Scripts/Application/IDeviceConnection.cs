using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // デバイス登録・認証(POST /devices, POST /devices/authenticate)。常に実APIを叩く唯一の
    // 実装しか存在せずMock/Realの切り替えを行わないため、Domainのリポジトリ抽象(Entityの
    // 永続化契約)ではなく、Application層の技術的なポートとして定義する。
    public interface IDeviceConnection
    {
        UniTask<string> RegisterAsync(string secretKey);

        UniTask<AuthenticationResult> AuthenticateAsync(string deviceId, string secretKey);
    }

    public readonly struct AuthenticationResult
    {
        public readonly string AccessToken;
        public readonly long ExpiresIn;

        public AuthenticationResult(string accessToken, long expiresIn)
        {
            AccessToken = accessToken;
            ExpiresIn = expiresIn;
        }
    }
}
