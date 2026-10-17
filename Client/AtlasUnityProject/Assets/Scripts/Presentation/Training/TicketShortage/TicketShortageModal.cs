using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Training
{
    // 育成チケットが足りないときのダイアログ。「ショップへ」で育成画面の上にショップを開く。
    public sealed class TicketShortageModal : Modal
    {
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private TextMeshProUGUI ownedText;
        [SerializeField] private TextMeshProUGUI requiredText;
        [SerializeField] private CommonButton shopButton;
        [SerializeField] private CommonButton cancelButton;

        public Observable<Unit> OnShopButtonClicked => shopButton.OnClickAsObservable();
        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();

        public void Refresh(TicketShortageViewDto dto)
        {
            messageText.text = $"{dto.Purpose}には{dto.Required}枚必要です";
            ownedText.text = $"所持 {dto.Owned}";
            requiredText.text = $"/ 必要 {dto.Required}";
        }
    }
}
