using System.Collections.Generic;

namespace Atlas.Domain
{
    public interface IPachimonMoveMapRepository
    {
        IReadOnlyList<PachimonMoveMapEntity> GetByPlayerPachimonId(string playerPachimonId);

        void Upsert(PachimonMoveMapEntity pachimonMoveMapEntity);

        void Delete(string playerPachimonMoveId);

        // サインインし直すとき(タイトルに戻ったとき)に、サーバーで無くなったものが残らないよう全件を消す。
        void Clear();
    }
}
