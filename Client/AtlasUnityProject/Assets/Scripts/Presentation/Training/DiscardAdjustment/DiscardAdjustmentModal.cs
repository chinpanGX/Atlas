using R3;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Training
{
    // 確定していない割り振りの調整があるまま、別のパチモンを選ぶ・タブを切り替える・画面を離れるときの確認。
    public sealed class DiscardAdjustmentModal : Modal
    {
        [SerializeField] private CommonButton discardButton;
        [SerializeField] private CommonButton cancelButton;

        public Observable<Unit> OnDiscardButtonClicked => discardButton.OnClickAsObservable();
        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();
    }
}
