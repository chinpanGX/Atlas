using Atlas.Application;
using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityScreenNavigator;
using VolTech.Client;

namespace Atlas.Navigation
{
    // 通信エラーの後の画面遷移。通信の共通処理(ApiCore)が、セッションを終わらせてから呼ぶ。
    // Homeシーンは、サインインしていなければタイトル、していればホームから始まる。
    public sealed class ErrorNavigator : IErrorNavigator
    {
        private readonly ISceneNavigator sceneNavigator;
        private readonly IPlayerSession playerSession;

        public ErrorNavigator(ISceneNavigator sceneNavigator, IPlayerSession playerSession)
        {
            this.sceneNavigator = sceneNavigator;
            this.playerSession = playerSession;
        }

        public UniTask GoToTitleAsync()
        {
            playerSession.SignOut();
            return sceneNavigator.ChangeSceneAsync(AddressDefinition.Home);
        }

        public UniTask GoToHomeAsync()
        {
            return sceneNavigator.ChangeSceneAsync(AddressDefinition.Home);
        }

        public UniTask OpenStoreAsync()
        {
            // ストアにはまだ出していないため、開く先が無い
            Debug.LogWarning("[ErrorNavigator] ストアのページはまだ無い");
            return UniTask.CompletedTask;
        }
    }
}
