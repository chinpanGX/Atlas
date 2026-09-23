using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // 動作確認用のジェム付与(POST /debug/grant_gems)。正式なゲーム内機能ではなく、
    // スカウトの動作確認をジェム切れせずに繰り返せるようにするための開発用API。
    public interface IDebugConnection
    {
        UniTask GrantGemsAsync();
    }
}
