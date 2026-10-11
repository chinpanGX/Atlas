using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Grpc
{
    public sealed class DebugConnection : IDebugConnection
    {
        private readonly DebugApi debugApi;

        public DebugConnection(DebugApi debugApi)
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
