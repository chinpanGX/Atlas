using System.Collections.Generic;
using Atlas.Application;
using Atlas.Domain;

namespace Atlas.Infrastructure
{
    public sealed class PachimonMoveMappingService : IPachimonMoveMappingService
    {
        private readonly IPachimonMoveMapRepository pachimonMoveMapRepository;

        public PachimonMoveMappingService(IPachimonMoveMapRepository pachimonMoveMapRepository)
        {
            this.pachimonMoveMapRepository = pachimonMoveMapRepository;
        }

        public IReadOnlyList<PachimonMoveMapEntity> GetByPlayerPachimonId(string playerPachimonId)
        {
            return pachimonMoveMapRepository.GetByPlayerPachimonId(playerPachimonId);
        }
    }
}
