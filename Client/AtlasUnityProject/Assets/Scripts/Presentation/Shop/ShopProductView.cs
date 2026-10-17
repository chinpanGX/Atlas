using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Shop
{
    // 商品1つのカード。名前・1枚あたりの値段・割引率と、値段を載せた購入ボタンを出す。
    public sealed class ShopProductView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI unitPriceText;
        [SerializeField] private GameObject discountBadge;
        [SerializeField] private TextMeshProUGUI discountText;
        [SerializeField] private TextMeshProUGUI shortageText;
        [SerializeField] private CommonButton purchaseButton;
        [SerializeField] private TextMeshProUGUI priceText;

        public Observable<Unit> OnPurchaseButtonClicked => purchaseButton.OnClickAsObservable();

        public void Refresh(ShopProductDto dto)
        {
            nameText.text = $"{dto.ItemName} ×{dto.Amount}";
            unitPriceText.text = $"1枚あたり {dto.UnitPrice:0.##}ジェム";
            discountBadge.SetActive(dto.DiscountPercent > 0);
            discountText.text = $"{dto.DiscountPercent}%おトク";
            // 押してからエラーを出すより先に、買えない理由と足りない量が分かるようにする。
            shortageText.gameObject.SetActive(dto.ShortageAmount > 0);
            shortageText.text = $"あと {dto.ShortageAmount}ジェム";
            purchaseButton.interactable = dto.ShortageAmount == 0;
            priceText.text = $"{dto.CostAmount}ジェム 購入";
        }
    }
}
