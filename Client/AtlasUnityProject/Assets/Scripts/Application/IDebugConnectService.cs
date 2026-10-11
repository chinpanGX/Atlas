using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    /// <summary>
    ///  開発用APIの操作を行う。
    /// </summary>
    public interface IDebugConnectService
    {
        UniTask<bool> GrantGemsAsync();
    }
}
