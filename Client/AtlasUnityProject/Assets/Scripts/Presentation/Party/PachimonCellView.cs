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
        // 編成中を示すフレームと、選択中を示すフレーム。両方同時に表示されることもある(選択中が手前)。
        [SerializeField] private GameObject partyFrame;
        [SerializeField] private GameObject selectedFrame;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void SetFrames(bool isInParty, bool isSelected)
        {
            partyFrame.SetActive(isInParty);
            selectedFrame.SetActive(isSelected);
        }

        public void Refresh(PachimonDto pachimon)
        {
            // サムネイルが用意されるまでは名前で識別できるようにする。
            nameText.text = pachimon.Name;
            thumbnailImage.sprite = pachimon.Thumbnail;
        }
    }
}
