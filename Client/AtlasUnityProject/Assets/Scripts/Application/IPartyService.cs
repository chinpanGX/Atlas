using System.Collections.Generic;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    public interface IPartyService
    {
        // 編成済みのスロットをslot昇順で返す(未編成のslotは要素自体が無い)。
        IReadOnlyList<PartyEntity> GetAll();

        // 編成を全置き換えで保存する。成功したらGetAllがサーバー確定後の編成を返す。
        // 失敗したときのダイアログは通信の共通処理が済ませているので、成否だけを返す。
        UniTask<bool> SaveAsync(IReadOnlyList<PartySlotInput> slots);
    }
}
