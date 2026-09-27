using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Scout
{
    public sealed class ScoutConfirmModal : Modal
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
