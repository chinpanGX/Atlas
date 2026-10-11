using System.Collections.Generic;
using Atlas.Api.V1;
using Atlas.Application;
using Atlas.Domain;
using Cysharp.Threading.Tasks;
using PartySlotInput = Atlas.Application.PartySlotInput;

namespace Atlas.Infrastructure
{
    internal sealed class PartyService : IPartyService
    {
        private readonly IPartyRepository partyRepository;
        private readonly PartyApi partyApi;

        public PartyService(IPartyRepository partyRepository, PartyApi partyApi)
        {
            this.partyRepository = partyRepository;
            this.partyApi = partyApi;
        }

        public IReadOnlyList<PartyEntity> GetAll()
        {
            return partyRepository.GetAll();
        }
        
        public async UniTask<bool> SaveAsync(IReadOnlyList<PartySlotInput> slots)
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
    }
}
