using System.Threading;
using Cysharp.Threading.Tasks;
using Supplement.Loader.Abstractions;
using UnityEngine.SceneManagement;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Navigation
{
    /// <summary>
    /// ISceneLoader.ChangeSceneは読み込むだけで前のシーンをUnloadしない(戻り値のISceneHandleを
    /// Disposeした時にUnloadされる)ため、現在重ねているシーンのハンドルをここで保持しておき、
    /// 次のシーンを読み込む前にDisposeする。RootLifetimeScopeにSingletonで登録する前提。
    /// </summary>
    public sealed class SceneNavigator : ISceneNavigator
    {
        private readonly ISceneLoader sceneLoader;
        private ISceneHandle currentScene;

        public SceneNavigator(ISceneLoader sceneLoader)
        {
            this.sceneLoader = sceneLoader;
        }

        public async UniTask ChangeSceneAsync(string address, CancellationToken cancellation = default)
        {
            // 呼び出し元のPresenterは今Unloadするシーンに属しているため、ここから先で
            // 呼び出し元のCancellationToken(=Pageの破棄)に巻き込まれてLoadが中断しないよう、
            // Unload前にだけキャンセルを確認する。
            cancellation.ThrowIfCancellationRequested();

            if (currentScene != null)
            {
                // USNの遷移アニメーションはUpdateDispatcher(DontDestroyOnLoad)に登録され、完了時に
                // 登録解除される。遷移中にシーンごとPage/Modalを破棄すると登録が残り、破棄済みの
                // RectTransformを毎フレーム操作して例外になるため、遷移が終わるまで待つ。
                var scene = currentScene.Result;
                await UniTask.WaitUntil(() => !IsInTransition(scene), cancellationToken: CancellationToken.None);

                currentScene.Dispose();
                currentScene = null;
            }

            currentScene = await sceneLoader.ChangeScene(address, true, CancellationToken.None);
        }

        private static bool IsInTransition(Scene scene)
        {
            if (!scene.isLoaded)
            {
                return false;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var container in root.GetComponentsInChildren<PageContainer>())
                {
                    if (container.IsInTransition)
                    {
                        return true;
                    }
                }

                foreach (var container in root.GetComponentsInChildren<ModalContainer>())
                {
                    if (container.IsInTransition)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
