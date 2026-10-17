using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.GlobalHeader
{
    // アウトゲームの画面に共通のヘッダー。Homeシーンに1つだけ置き、表示中の画面に合わせて中身を切り替える。
    public sealed class GlobalHeaderView : MonoBehaviour
    {
        [SerializeField] private CommonButton backButton;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI gemAmountText;
        [SerializeField] private TextMeshProUGUI ticketAmountText;
        [SerializeField] private CommonButton shopButton;

        public Observable<Unit> OnBackButtonClicked => backButton.OnClickAsObservable();
        public Observable<Unit> OnShopButtonClicked => shopButton.OnClickAsObservable();

        public void Refresh(GlobalHeaderViewDto dto)
        {
            gameObject.SetActive(dto.IsVisible);
            if (!dto.IsVisible)
            {
                return;
            }

            titleText.text = dto.Title;
            gemAmountText.text = dto.Gems.ToString();
            ticketAmountText.text = dto.Tickets.ToString();
            backButton.gameObject.SetActive(dto.ShowsBack);
            backButton.interactable = dto.IsInteractable;
            shopButton.interactable = dto.IsInteractable && dto.CanOpenShop;
        }
    }
}
