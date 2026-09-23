using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Party
{
    // 画面中央の所持パチモン一覧の1マス。PachimonListViewがテンプレートを複製して使う。
    public sealed class PachimonCellView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private Image thumbnailImage;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void Refresh(PachimonDto pachimon)
        {
            // サムネイルが用意されるまでは名前で識別できるようにする。
            nameText.text = pachimon.Name;
            thumbnailImage.sprite = pachimon.Thumbnail;
        }
    }
}
