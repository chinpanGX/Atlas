using Atlas.Navigation;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Scout
{
    // 確定する=true。「いいえ」以外の閉じ方(背景タップ等)も確定しない扱い(CanceledResult=false)。
    public sealed class ScoutConfirmModal : ResultModal<bool>
    {
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;

        public Observable<Unit> OnYesButtonClicked => yesButton.OnClickAsObservable();
        public Observable<Unit> OnNoButtonClicked => noButton.OnClickAsObservable();

        public void SetMessage(string message)
        {
            messageText.text = message;
        }
    }
}
