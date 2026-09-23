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

        // 新しいターンの入力は「たたかう/こうたい」から始める。
        public void ShowCommandPanel()
        {
            commandView.ShowCommandPanel();
        }
    }
}
