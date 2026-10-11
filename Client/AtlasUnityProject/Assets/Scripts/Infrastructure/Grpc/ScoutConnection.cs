using System.Collections.Generic;
using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;
using ScoutBanner = Atlas.Application.ScoutBanner;
using ScoutCandidate = Atlas.Application.ScoutCandidate;

namespace Atlas.Infrastructure.Grpc
{
    public sealed class ScoutConnection : IScoutConnection
    {
        private readonly ScoutApi scoutApi;

        public ScoutConnection(ScoutApi scoutApi)
        {
            this.scoutApi = scoutApi;
        }

        public async UniTask<IReadOnlyList<ScoutBanner>> GetBannersAsync()
        {
            var result = await scoutApi.ListBannersAsync(new ListBannersRequest());
            if (!result.IsSuccess)
            {
                return null;
            }

            var banners = new List<ScoutBanner>(result.Value.Banners.Count);
            foreach (var banner in result.Value.Banners)
            {
                banners.Add(new ScoutBanner(banner.BannerId, banner.Name, banner.CostPerRoll));
            }
            return banners;
        }

        public async UniTask<ScoutRoll> RollAsync(string bannerId)
        {
            var result = await scoutApi.RollAsync(new RollRequest { BannerId = bannerId });
            if (!result.IsSuccess)
            {
                return null;
            }

            var candidates = new List<ScoutCandidate>(result.Value.Candidates.Count);
            foreach (var candidate in result.Value.Candidates)
            {
                candidates.Add(new ScoutCandidate(candidate.Index, candidate.PachimonId, new List<int>(candidate.MoveIds)));
            }
            return new ScoutRoll(result.Value.RollId, candidates);
        }

        public async UniTask<bool> SelectAsync(string rollId, int candidateIndex)
        {
            var result = await scoutApi.SelectAsync(new SelectRequest { RollId = rollId, Index = candidateIndex });
            return result.IsSuccess;
        }
    }
}
