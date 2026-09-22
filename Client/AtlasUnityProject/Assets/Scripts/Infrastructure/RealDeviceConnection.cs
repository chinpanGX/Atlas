using Atlas.Domain;
using Atlas.Infrastructure.Api;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class RealDeviceConnection : IDeviceConnection
    {
        private readonly DeviceApiClient client;

        public RealDeviceConnection(string baseUrl)
        {
            client = new DeviceApiClient(baseUrl);
        }

        public async UniTask<string> RegisterAsync(string secretKey)
        {
            var response = await client.RegisterDeviceAsync(new RegisterDeviceRequest { SecretKey = secretKey });
            return response.DeviceId;
        }

        public async UniTask<AuthenticationResult> AuthenticateAsync(string deviceId, string secretKey)
        {
            var response = await client.AuthenticateDeviceAsync(
                new AuthenticateDeviceRequest { DeviceId = deviceId, SecretKey = secretKey });
            return new AuthenticationResult(response.AccessToken, response.ExpiresIn);
        }
    }
}
