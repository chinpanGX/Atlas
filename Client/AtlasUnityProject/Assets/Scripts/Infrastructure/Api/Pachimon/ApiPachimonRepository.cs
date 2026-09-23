using System.Collections.Generic;
using System.Linq;
using Atlas.Domain;

namespace Atlas.Infrastructure.Api
{
    public sealed class ApiPachimonRepository : IPachimonRepository
    {
        private readonly Dictionary<string, PachimonEntity> pachimonEntities = new();

        public PachimonEntity Get(string playerPachimonId)
        {
            return pachimonEntities.GetValueOrDefault(playerPachimonId);
        }

        public IReadOnlyList<PachimonEntity> GetAll()
        {
            return pachimonEntities.Values.ToList();
        }

        public void Upsert(PachimonEntity pachimonEntity)
        {
            pachimonEntities[pachimonEntity.PlayerPachimonId] = pachimonEntity;
        }

        public void Delete(string playerPachimonId)
        {
            pachimonEntities.Remove(playerPachimonId);
        }
    }
}
