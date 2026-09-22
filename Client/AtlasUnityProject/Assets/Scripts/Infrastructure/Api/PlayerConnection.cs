using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    public sealed class PlayerConnection : IPlayerConnection
    {
        private readonly PlayerApiClient client;
        private readonly IPlayerDiffApplier playerDiffApplier;

        public PlayerConnection(string baseUrl, AccessTokenStore accessTokenStore, IPlayerDiffApplier playerDiffApplier)
        {
            client = new PlayerApiClient(baseUrl, () => accessTokenStore.CurrentToken);
            this.playerDiffApplier = playerDiffApplier;
        }

        public UniTask SignUpAsync(string nickname)
        {
            return client.SignupAsync(new CreatePlayerRequest { Nickname = nickname });
        }

        public async UniTask<SignInResult> SignInAsync()
        {
            var response = await client.SignInAsync();
            await playerDiffApplier.ApplyAsync(response.PlayerDiff);
            return new SignInResult(response.PlayerId, response.Nickname);
        }
    }
}
