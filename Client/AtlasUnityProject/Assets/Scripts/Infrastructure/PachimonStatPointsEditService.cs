using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    internal sealed class PachimonStatPointsEditService : IPachimonStatPointsEditService
    {
        private readonly PartyApi partyApi;

        public PachimonStatPointsEditService(PartyApi partyApi)
        {
            this.partyApi = partyApi;
        }

        public async UniTask<bool> EditAsync(string playerPachimonId, Domain.StatPoints statPoints)
        {
            var result = await partyApi.SetPachimonStatPointsAsync(new SetPachimonStatPointsRequest
            {
                PlayerPachimonId = playerPachimonId,
                StatPoints = new StatPoints
                {
                    Hp = statPoints.Hp,
                    Atk = statPoints.Atk,
                    Def = statPoints.Def,
                    Spatk = statPoints.Spatk,
                    Spdef = statPoints.Spdef,
                    Speed = statPoints.Speed,
                },
            });
            return result.IsSuccess;
        }
    }
}
