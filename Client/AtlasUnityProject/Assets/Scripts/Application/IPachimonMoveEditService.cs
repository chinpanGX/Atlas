using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    public interface IPachimonMoveEditService
    {
        // 技の付け替えを保存する(1回の呼び出しで1スロット分)。成功したらGetByPlayerPachimonIdが
        // サーバー確定後の内容を返す。失敗したときは成否だけを返す。
        UniTask<bool> EditAsync(string playerPachimonId, int slot, int moveId);
    }
}