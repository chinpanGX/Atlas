using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Grpc
{
    internal sealed class DebugConnectService : IDebugConnectService
    {
        private readonly DebugApi debugApi;

        public DebugConnectService(DebugApi debugApi)
        {
            this.debugApi = debugApi;
        }

        public async UniTask<bool> GrantGemsAsync()
        {
            var result = await debugApi.GrantGemsAsync(new GrantGemsRequest());
            return result.IsSuccess;
        }
    }
}
