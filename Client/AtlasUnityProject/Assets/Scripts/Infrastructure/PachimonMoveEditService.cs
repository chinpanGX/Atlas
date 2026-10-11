using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    internal sealed class PachimonMoveEditService : IPachimonMoveEditService
    {
        private readonly PartyApi partyApi;
        
        public PachimonMoveEditService(PartyApi partyApi)
        {
            this.partyApi = partyApi;
        }

        public async UniTask<bool> EditAsync(string playerPachimonId, int slot, int moveId)
        {
            var result = await partyApi.SetPachimonMoveAsync(new SetPachimonMoveRequest
            {
                PlayerPachimonId = playerPachimonId,
                Slot = slot,
                MoveId = moveId,
            });
            return result.IsSuccess;
        }
    }
}