using Atlas.Domain;
using Atlas.Infrastructure.Api;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class RealPlayerRepository : IPlayerRepository
    {
        private readonly PlayerApiClient client;

        public RealPlayerRepository(string baseUrl, AccessTokenStore accessTokenStore)
        {
            client = new PlayerApiClient(baseUrl, () => accessTokenStore.CurrentToken);
        }

        public async UniTask<PlayerData> GetAsync()
        {
            var response = await client.GetMeAsync();
            return ToPlayerData(response);
        }

        public async UniTask<PlayerData> CreateAsync(string nickname)
        {
            var response = await client.CreatePlayerAsync(new CreatePlayerRequest { Nickname = nickname });
            return ToPlayerData(response);
        }

        private static PlayerData ToPlayerData(PlayerResponse response)
        {
            return new PlayerData(response.PlayerId, response.Nickname, response.Gems);
        }
    }
}
