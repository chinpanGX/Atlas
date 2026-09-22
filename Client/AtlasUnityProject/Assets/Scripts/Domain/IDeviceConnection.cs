using Cysharp.Threading.Tasks;

namespace Atlas.Domain
{
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
