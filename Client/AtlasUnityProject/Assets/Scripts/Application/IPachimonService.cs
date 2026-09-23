using System.Collections.Generic;
using Atlas.Domain;

namespace Atlas.Application
{
    public interface IPachimonService
    {
        // 未所持ならnull。
        PachimonEntity Get(string playerPachimonId);

        IReadOnlyList<PachimonEntity> GetAll();
    }
}
