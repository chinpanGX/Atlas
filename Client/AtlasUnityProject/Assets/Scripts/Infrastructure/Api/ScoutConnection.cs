using System.Collections.Generic;
using System.Linq;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    public sealed class ScoutConnection : IScoutConnection
    {
        private readonly ScoutApiClient client;
        private readonly AccessTokenRefresher accessTokenRefresher;
        private readonly IPlayerDiffApplier playerDiffApplier;

        public ScoutConnection(
            string baseUrl,
            AccessTokenStore accessTokenStore,
            AccessTokenRefresher accessTokenRefresher,
            IPlayerDiffApplier playerDiffApplier)
        {
            client = new ScoutApiClient(baseUrl, () => accessTokenStore.CurrentToken);
            this.accessTokenRefresher = accessTokenRefresher;
            this.playerDiffApplier = playerDiffApplier;
        }

        public async UniTask<IReadOnlyList<ScoutBanner>> GetBannersAsync()
        {
            var response = await accessTokenRefresher.SendAsync(() => client.ListBannersAsync());
            return response.Banners
                .Select(b => new ScoutBanner(b.BannerId, b.Name, b.CostPerRoll))
                .ToList();
        }

        public async UniTask<ScoutRoll> RollAsync(string bannerId)
        {
            var response = await accessTokenRefresher.SendAsync(
                () => client.CreateRollAsync(new CreateRollRequest { BannerId = bannerId }));
            await playerDiffApplier.ApplyAsync(response.PlayerDiff);
            var candidates = response.Candidates
                .OrderBy(c => c.Index)
                .Select(c => new ScoutCandidate(c.Index, c.PachimonId, c.Moves))
                .ToList();
            return new ScoutRoll(response.RollId, candidates);
        }

        public async UniTask SelectAsync(string rollId, int candidateIndex)
        {
            var playerDiff = await accessTokenRefresher.SendAsync(
                () => client.SelectRollAsync(rollId, new SelectRollRequest { Index = candidateIndex }));
            await playerDiffApplier.ApplyAsync(playerDiff);
        }
    }
}
