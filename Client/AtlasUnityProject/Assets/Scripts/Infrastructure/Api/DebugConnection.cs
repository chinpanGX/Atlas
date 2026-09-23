using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    public sealed class DebugConnection : IDebugConnection
    {
        private readonly DebugApiClient client;
        private readonly AccessTokenRefresher accessTokenRefresher;
        private readonly IPlayerDiffApplier playerDiffApplier;

        public DebugConnection(
            string baseUrl,
            AccessTokenStore accessTokenStore,
            AccessTokenRefresher accessTokenRefresher,
            IPlayerDiffApplier playerDiffApplier)
        {
            client = new DebugApiClient(baseUrl, () => accessTokenStore.CurrentToken);
            this.accessTokenRefresher = accessTokenRefresher;
            this.playerDiffApplier = playerDiffApplier;
        }

        public async UniTask GrantGemsAsync()
        {
            var playerDiff = await accessTokenRefresher.SendAsync(() => client.GrantGemsAsync());
            await playerDiffApplier.ApplyAsync(playerDiff);
        }
    }
}
