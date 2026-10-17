using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    public interface IPachimonStatPointsEditService
    {
        // ステータスポイントの割り振りを、送った値(割り振りの後の値)に変えて保存する。増やした分の育成チケットを使う。
        // 成功したらIPachimonService・IItemServiceがサーバー確定後の内容を返す。失敗したときは成否だけを返す。
        UniTask<bool> EditAsync(string playerPachimonId, StatPoints statPoints);
    }
}
