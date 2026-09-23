using System.Collections.Generic;
using Atlas.Application;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class PartyService : IPartyService
    {
        private readonly IPartyRepository partyRepository;
        private readonly IPlayerConnection playerConnection;

        public PartyService(IPartyRepository partyRepository, IPlayerConnection playerConnection)
        {
            this.partyRepository = partyRepository;
            this.playerConnection = playerConnection;
        }

        public IReadOnlyList<PartyEntity> GetAll()
        {
            return partyRepository.GetAll();
        }

        // IPartyRepositoryへの反映はPlayerConnectionがplayerDiffの適用で行う。
        public UniTask SaveAsync(IReadOnlyList<PartySlotInput> slots)
        {
            return playerConnection.EditPartyAsync(slots);
        }
    }
}
