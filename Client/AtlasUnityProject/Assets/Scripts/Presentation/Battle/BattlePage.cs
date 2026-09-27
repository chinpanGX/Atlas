using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Battle
{
    public sealed class BattlePage : Page
    {
        [SerializeField] private SelfInfoView selfInfoView;
        [SerializeField] private OpponentInfoView opponentInfoView;
        [SerializeField] private CommandView commandView;
        [SerializeField] private BattleMessageView messageView;
        [SerializeField] private CommonButton forfeitButton;

        // 押された技のSlotNoを流す。
        public Observable<int> OnMoveButtonClicked => commandView.OnMoveClicked;
        public Observable<Unit> OnSwitchButtonClicked => commandView.OnSwitchClicked;
        public Observable<Unit> OnForfeitButtonClicked => forfeitButton.OnClickAsObservable();

        public void Refresh(BattleUIStateDto stateDto)
        {
            selfInfoView.Refresh(stateDto.SelfInfo);
            opponentInfoView.Refresh(stateDto.OpponentInfo);
            commandView.Refresh(stateDto.Commands);
        }

        // 行動(技・交代)の入力可否。行動送信後にターン結果が届くまでの間や、強制交代の選択中は押せなくする。
        // 投了はいつでもできるよう対象外。
        public void SetCommandsInteractable(bool interactable)
        {
            commandView.SetInteractable(interactable);
        }

        // 新しいターンの入力は「たたかう/こうたい」から始める。メッセージ枠は隠す。
        public void ShowCommandPanel()
        {
            messageView.Hide();
            commandView.SetVisible(true);
            commandView.ShowCommandPanel();
        }

        // コマンドを隠してメッセージを出したままにする(相手の行動待ち等、次の出来事が来るまで出し続ける文)。
        public void ShowMessage(string message)
        {
            commandView.SetVisible(false);
            messageView.Show(message);
        }

        // コマンドを隠してメッセージを出し、読み終わる(一定時間経つかタップされる)まで待つ。
        public UniTask PlayMessageAsync(string message, CancellationToken cancellation)
        {
            commandView.SetVisible(false);
            return messageView.ShowAndWaitAsync(message, cancellation);
        }
    }
}
