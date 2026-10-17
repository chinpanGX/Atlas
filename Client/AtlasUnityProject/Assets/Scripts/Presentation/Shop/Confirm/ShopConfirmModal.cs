using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Shop
{
    public sealed class ShopConfirmModal : Modal
    {
        [SerializeField] private TextMeshProUGUI productText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private TextMeshProUGUI costItemNameText;
        [SerializeField] private TextMeshProUGUI costItemChangeText;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI itemChangeText;
        [SerializeField] private CommonButton purchaseButton;
        [SerializeField] private CommonButton cancelButton;

        public Observable<Unit> OnPurchaseButtonClicked => purchaseButton.OnClickAsObservable();
        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();

        public void Refresh(ShopConfirmViewDto dto)
        {
            productText.text = $"{dto.ItemName} ×{dto.Amount}";
            messageText.text = $"{dto.CostAmount}{dto.CostItemName}で購入しますか？";
            costItemNameText.text = dto.CostItemName;
            costItemChangeText.text = $"{dto.CostItemBefore} → {dto.CostItemAfter}";
            itemNameText.text = dto.ItemName;
            itemChangeText.text = $"{dto.ItemBefore} → {dto.ItemAfter}";
        }
    }
}
