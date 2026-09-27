using R3;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Home
{
    // 「対戦相手を探しています」の表示とキャンセルボタンだけを持つ。
    public sealed class MatchmakingModal : Modal
    {
        [SerializeField] private CommonButton cancelButton;

        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();
    }
}
