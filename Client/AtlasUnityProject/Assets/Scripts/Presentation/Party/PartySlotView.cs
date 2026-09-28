using Atlas.Presentation.Common;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Party
{
    // 画面左側のパーティ枠1つ分。未編成の枠は名前を空にし、サムネイルを隠す。
    public sealed class PartySlotView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private PachimonThumbnailView thumbnail;
        [SerializeField] private GameObject selectedFrame;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void SetSelected(bool isSelected)
        {
            selectedFrame.SetActive(isSelected);
        }

        public void Refresh(PachimonDto pachimon)
        {
            var hasPachimon = pachimon is not null;
            nameText.text = hasPachimon ? pachimon.Name : string.Empty;
            thumbnail.gameObject.SetActive(hasPachimon);
            thumbnail.Show(pachimon?.PachimonId);
        }
    }
}
