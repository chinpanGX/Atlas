using System.Threading;
using Cysharp.Threading.Tasks;

namespace Atlas.Navigation
{
    /// <summary>
    /// Scene単位の遷移(Bootstrap→Home、Home⇔Battle)。design/client-architecture.md「シーン構成」参照。
    /// Bootstrapシーンは常駐させたまま、その上に重ねるシーンを1つだけ保持し、切り替え時は
    /// 前のシーンをUnloadしてから次のシーンをLoadする。Scene内の画面遷移はIScreenNavigatorで行う。
    /// </summary>
    public interface ISceneNavigator
    {
        UniTask ChangeSceneAsync(string address, CancellationToken cancellation = default);
    }
}
