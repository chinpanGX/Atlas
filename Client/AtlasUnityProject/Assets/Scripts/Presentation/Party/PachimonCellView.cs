using Atlas.Presentation.Common;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Party
{
    // 画面中央の所持パチモン一覧の1マス。PachimonListViewがテンプレートを複製して使う。
    public sealed class PachimonCellView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private PachimonThumbnailView thumbnail;
        // 編成中を示すフレームと、選択中を示すフレーム。両方同時に表示されることもある(選択中が手前)。
        [SerializeField] private GameObject partyFrame;
        [SerializeField] private GameObject selectedFrame;
        // スカウトの候補のように新しい種族を示す画面だけが持つ。育成画面の一覧には無い。
        [SerializeField] private GameObject newBadge;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void SetFrames(bool isInParty, bool isSelected)
        {
            partyFrame.SetActive(isInParty);
            selectedFrame.SetActive(isSelected);
        }

        public void Refresh(PachimonDto pachimon)
        {
            nameText.text = pachimon.Name;
            thumbnail.Show(pachimon.PachimonId);
            if (newBadge != null)
            {
                newBadge.SetActive(pachimon.IsNew);
            }
        }
    }
}
