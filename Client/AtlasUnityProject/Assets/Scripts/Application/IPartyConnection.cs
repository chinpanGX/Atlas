using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // パーティの編成と技の付け替え。変わった所持データは通信の共通処理が各Repositoryへ反映する。
    // 失敗したときのダイアログは通信の共通処理が済ませているので、成否だけを返す。
    public interface IPartyConnection
    {
        // 編成の全置き換え。slotsに含まれない枠は解除される(1〜6枠、同じパチモンの重複不可)。
        UniTask<bool> EditPartyAsync(IReadOnlyList<PartySlotInput> slots);

        // 技の付け替え(1回の呼び出しで1スロット分、slotは1〜4)。moveIdは技グループの候補技だけを受け付ける。
        UniTask<bool> EditPachimonMoveAsync(string playerPachimonId, int slot, int moveId);
    }
}
