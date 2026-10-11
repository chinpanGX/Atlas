using System.Collections.Generic;
using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;
using PartySlotInput = Atlas.Application.PartySlotInput;

namespace Atlas.Infrastructure.Grpc
{
    public sealed class PartyConnection : IPartyConnection
    {
        private readonly PartyApi partyApi;

        public PartyConnection(PartyApi partyApi)
        {
            this.partyApi = partyApi;
        }

        public async UniTask<bool> EditPartyAsync(IReadOnlyList<PartySlotInput> slots)
        {
            var request = new SetPartyRequest();
            foreach (var slot in slots)
            {
                request.PartySlots.Add(new Api.V1.PartySlotInput
                {
                    Slot = slot.Slot,
                    PlayerPachimonId = slot.PlayerPachimonId,
                });
            }
            var result = await partyApi.SetPartyAsync(request);
            return result.IsSuccess;
        }

        public async UniTask<bool> EditPachimonMoveAsync(string playerPachimonId, int slot, int moveId)
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
