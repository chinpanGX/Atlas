using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    public sealed class PartyDiffApplier
    {
        private readonly IPartyRepository repository;

        public PartyDiffApplier(IPartyRepository repository)
        {
            this.repository = repository;
        }

        public UniTask ApplyAsync(PartySlotsDiffDto dto)
        {
            foreach (var partySlot in dto.Upserted)
            {
                repository.Upsert(new PartyEntity(partySlot.PartySlotId, partySlot.PlayerPachimonId, partySlot.Slot));
            }
            foreach (var partySlotId in dto.Removed)
            {
                repository.Delete(partySlotId);
            }
            return UniTask.CompletedTask;
        }
    }
}
