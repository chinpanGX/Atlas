using System.Collections.Generic;
using Atlas.Domain;
using ZLinq;

namespace Atlas.Infrastructure
{
    public sealed class PachimonRepository : IPachimonRepository
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

        public void Clear()
        {
            pachimonEntities.Clear();
        }
    }
}
