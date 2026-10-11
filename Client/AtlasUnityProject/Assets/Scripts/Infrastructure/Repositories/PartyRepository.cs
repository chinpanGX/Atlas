using System.Collections.Generic;
using Atlas.Domain;
using ZLinq;

namespace Atlas.Infrastructure
{
    public sealed class PartyRepository : IPartyRepository
    {
        private readonly Dictionary<string, PartyEntity> partyEntities = new();

        public IReadOnlyList<PartyEntity> GetAll()
        {
            return partyEntities.Values.OrderBy(entity => entity.Slot).ToList();
        }

        public void Upsert(PartyEntity partyEntity)
        {
            partyEntities[partyEntity.PartySlotId] = partyEntity;
        }

        public void Delete(string partySlotId)
        {
            partyEntities.Remove(partySlotId);
        }

        public void Clear()
        {
            partyEntities.Clear();
        }
    }
}
