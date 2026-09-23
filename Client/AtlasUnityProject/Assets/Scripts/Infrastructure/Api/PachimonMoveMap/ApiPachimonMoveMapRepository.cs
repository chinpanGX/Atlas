using System.Collections.Generic;
using System.Linq;
using Atlas.Domain;

namespace Atlas.Infrastructure.Api
{
    public sealed class ApiPachimonMoveMapRepository : IPachimonMoveMapRepository
    {
        private readonly Dictionary<string, PachimonMoveMapEntity> pachimonMoveMapEntities = new();

        public IReadOnlyList<PachimonMoveMapEntity> GetByPlayerPachimonId(string playerPachimonId)
        {
            return pachimonMoveMapEntities.Values
                .Where(entity => entity.PlayerPachimonId == playerPachimonId)
                .OrderBy(entity => entity.Slot)
                .ToList();
        }

        public void Upsert(PachimonMoveMapEntity pachimonMoveMapEntity)
        {
            pachimonMoveMapEntities[pachimonMoveMapEntity.PlayerPachimonMoveId] = pachimonMoveMapEntity;
        }

        public void Delete(string playerPachimonMoveId)
        {
            pachimonMoveMapEntities.Remove(playerPachimonMoveId);
        }
    }
}
