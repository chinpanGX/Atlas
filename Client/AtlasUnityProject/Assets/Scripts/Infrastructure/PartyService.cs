using System.Collections.Generic;
using Atlas.Application;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class PartyService : IPartyService
    {
        private readonly IPartyRepository partyRepository;
        private readonly IPartyConnection partyConnection;

        public PartyService(IPartyRepository partyRepository, IPartyConnection partyConnection)
        {
            this.partyRepository = partyRepository;
            this.partyConnection = partyConnection;
        }

        public IReadOnlyList<PartyEntity> GetAll()
        {
            return partyRepository.GetAll();
        }

        // IPartyRepositoryへの反映は、通信の共通処理がplayerDiffの適用で行う。
        public UniTask<bool> SaveAsync(IReadOnlyList<PartySlotInput> slots)
        {
            return partyConnection.EditPartyAsync(slots);
        }
    }
}
