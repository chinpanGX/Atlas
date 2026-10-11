using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // 動作確認用のジェム付与。正式なゲーム内機能ではなく、スカウトの動作確認をジェム切れせずに繰り返せるようにするための
    // 開発用API。付与したジェムは通信の共通処理がplayerDiffとして反映する。失敗したときのダイアログも通信の共通処理が出す。
    public interface IDebugConnection
    {
        UniTask<bool> GrantGemsAsync();
    }
}
