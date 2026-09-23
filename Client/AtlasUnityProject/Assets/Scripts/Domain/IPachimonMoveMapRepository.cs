using System.Collections.Generic;

namespace Atlas.Domain
{
    public interface IPachimonMoveMapRepository
    {
        IReadOnlyList<PachimonMoveMapEntity> GetByPlayerPachimonId(string playerPachimonId);

        void Upsert(PachimonMoveMapEntity pachimonMoveMapEntity);

        void Delete(string playerPachimonMoveId);
    }
}
