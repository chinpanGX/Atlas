using Atlas.Api.V1;
using Atlas.Domain;

namespace Atlas.Infrastructure.Grpc
{
    public sealed class PartyDiffApplier
    {
        private readonly IPartyRepository repository;

        public PartyDiffApplier(IPartyRepository repository)
        {
            this.repository = repository;
        }

        public void Apply(PartySlotsDiff diff)
        {
            foreach (var partySlot in diff.Upserted)
            {
                repository.Upsert(ToEntity(partySlot));
            }
            foreach (var partySlotId in diff.Removed)
            {
                repository.Delete(partySlotId);
            }
        }

        public static PartyEntity ToEntity(PartySlot partySlot)
        {
            return new PartyEntity(partySlot.PartySlotId, partySlot.PlayerPachimonId, partySlot.Slot);
        }
    }
}
