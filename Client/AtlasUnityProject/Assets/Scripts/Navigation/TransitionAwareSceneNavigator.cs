using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityScreenNavigator;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Navigation
{
    /// <summary>
    /// 画面遷移の途中でシーンを切り替えないよう、Page/Modalの遷移が終わるのを待ってから
    /// <see cref="SceneNavigator"/>に委ねる。USNの遷移アニメーションはUpdateDispatcher(DontDestroyOnLoad)に
    /// 登録され、完了時に登録解除される。遷移中にシーンごとPage/Modalを破棄すると登録が残り、
    /// 破棄済みのRectTransformを毎フレーム操作して例外になるため。
    /// </summary>
    public sealed class TransitionAwareSceneNavigator : ISceneNavigator
    {
        private readonly SceneNavigator sceneNavigator;

        public TransitionAwareSceneNavigator(SceneNavigator sceneNavigator)
        {
            this.sceneNavigator = sceneNavigator;
        }

        public async UniTask ChangeSceneAsync(string address, CancellationToken cancellation = default)
        {
            await UniTask.WaitUntil(() => !IsAnyContainerInTransition(), cancellationToken: cancellation);
            await sceneNavigator.ChangeSceneAsync(address, cancellation);
        }

        private static bool IsAnyContainerInTransition()
        {
            return PageContainer.Instances.Any(x => x.IsInTransition)
                   || ModalContainer.Instances.Any(x => x.IsInTransition);
        }
    }
}
