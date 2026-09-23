using System.Collections.Generic;
using Atlas.Application;
using Atlas.Domain;

namespace Atlas.Infrastructure
{
    public sealed class PachimonService : IPachimonService
    {
        private readonly IPachimonRepository pachimonRepository;

        public PachimonService(IPachimonRepository pachimonRepository)
        {
            this.pachimonRepository = pachimonRepository;
        }

        public PachimonEntity Get(string playerPachimonId)
        {
            return pachimonRepository.Get(playerPachimonId);
        }

        public IReadOnlyList<PachimonEntity> GetAll()
        {
            return pachimonRepository.GetAll();
        }
    }
}
