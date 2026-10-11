using Atlas.Application;
using Atlas.Domain;

namespace Atlas.Infrastructure
{
    public sealed class PlayerSession : IPlayerSession
    {
        private readonly IPlayerProfileRepository playerProfileRepository;
        private readonly IItemRepository itemRepository;
        private readonly IPachimonRepository pachimonRepository;
        private readonly IPachimonMoveMapRepository pachimonMoveMapRepository;
        private readonly IPartyRepository partyRepository;

        public PlayerSession(
            IPlayerProfileRepository playerProfileRepository,
            IItemRepository itemRepository,
            IPachimonRepository pachimonRepository,
            IPachimonMoveMapRepository pachimonMoveMapRepository,
            IPartyRepository partyRepository)
        {
            this.playerProfileRepository = playerProfileRepository;
            this.itemRepository = itemRepository;
            this.pachimonRepository = pachimonRepository;
            this.pachimonMoveMapRepository = pachimonMoveMapRepository;
            this.partyRepository = partyRepository;
        }

        public bool IsSignedIn { get; private set; }

        public void MarkSignedIn()
        {
            IsSignedIn = true;
        }

        public void SignOut()
        {
            IsSignedIn = false;
            playerProfileRepository.Clear();
            itemRepository.Clear();
            pachimonRepository.Clear();
            pachimonMoveMapRepository.Clear();
            partyRepository.Clear();
        }
    }
}
