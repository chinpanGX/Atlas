using System.Collections.Generic;
using Atlas.Domain;

namespace Atlas.Application
{
    public interface IPachimonMoveMappingService
    {
        // 所持パチモンに割り当て済みの技を返す(未設定の技slotは要素自体が無い)。
        IReadOnlyList<PachimonMoveMapEntity> GetByPlayerPachimonId(string playerPachimonId);
    }
}
