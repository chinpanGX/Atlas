using R3;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Home
{
    // 「対戦相手を探しています」の表示とキャンセルボタンだけを持つ。マッチングの開始・キャンセル・成立後の
    // 遷移は、このModalをPushしたHomePresenterが行う(USNは遷移中のPush/Popを拒否するため、Pushの完了を
    // 待ってからマッチングを始め、Popの完了を待ってからシーンを切り替える必要があり、その順序を1箇所で
    // 管理するため)。
    public sealed class MatchmakingModal : Modal
    {
        [SerializeField] private CommonButton cancelButton;

        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();
    }
}
