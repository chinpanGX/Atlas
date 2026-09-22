using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Mock
{
    public sealed class MockDeviceConnection : IDeviceConnection
    {
        public UniTask<string> RegisterAsync(string secretKey)
        {
            return UniTask.FromResult("mock-device-id");
        }

        public UniTask<AuthenticationResult> AuthenticateAsync(string deviceId, string secretKey)
        {
            return UniTask.FromResult(new AuthenticationResult("mock-access-token", 3600));
        }
    }
}
