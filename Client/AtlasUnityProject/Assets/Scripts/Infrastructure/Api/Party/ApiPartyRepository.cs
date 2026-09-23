using System.Collections.Generic;
using System.Linq;
using Atlas.Domain;

namespace Atlas.Infrastructure.Api
{
    public sealed class ApiPartyRepository : IPartyRepository
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
    }
}
