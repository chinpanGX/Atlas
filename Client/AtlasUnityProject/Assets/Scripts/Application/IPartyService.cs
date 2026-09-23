using System.Collections.Generic;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    public interface IPartyService
    {
        // 編成済みのスロットをslot昇順で返す(未編成のslotは要素自体が無い)。
        IReadOnlyList<PartyEntity> GetAll();

        // 編成を全置き換えで保存する。完了後はGetAllがサーバー確定後の編成を返す。
        UniTask SaveAsync(IReadOnlyList<PartySlotInput> slots);
    }
}
