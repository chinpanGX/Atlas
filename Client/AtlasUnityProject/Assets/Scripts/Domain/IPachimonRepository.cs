using System.Collections.Generic;

namespace Atlas.Domain
{
    // 所持パチモン(player_pachimon)専用のリポジトリ。
    public interface IPachimonRepository
    {
        // 未所持ならnull。
        PachimonEntity Get(string playerPachimonId);

        IReadOnlyList<PachimonEntity> GetAll();

        void Upsert(PachimonEntity pachimonEntity);

        void Delete(string playerPachimonId);
    }
}
