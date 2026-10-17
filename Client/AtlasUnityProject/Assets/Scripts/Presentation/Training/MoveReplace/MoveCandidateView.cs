using Atlas.Presentation.Common;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Training
{
    // 付け替える技の候補の1行。選べない技は消さずに薄くして理由を出す(なぜ並んでいないのか分からなくならないように)。
    public sealed class MoveCandidateView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private PachimonTypeBadgeView typeBadge;
        [SerializeField] private TextMeshProUGUI categoryText;
        [SerializeField] private TextMeshProUGUI powerText;
        [SerializeField] private TextMeshProUGUI accuracyText;
        [SerializeField] private TextMeshProUGUI ppText;
        [SerializeField] private TextMeshProUGUI disabledReasonText;
        [SerializeField] private GameObject selectedFrame;
        [SerializeField] private float disabledAlpha = 0.45f;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void Refresh(MoveCandidateDto candidate)
        {
            nameText.text = candidate.Name;
            typeBadge.Show(candidate.Type);
            categoryText.text = candidate.CategoryName;
            powerText.text = candidate.BasePower.ToString();
            accuracyText.text = candidate.Accuracy.ToString();
            ppText.text = candidate.MaxPp.ToString();

            var selectable = candidate.DisabledReason is null;
            button.interactable = selectable;
            canvasGroup.alpha = selectable ? 1f : disabledAlpha;
            disabledReasonText.gameObject.SetActive(!selectable);
            disabledReasonText.text = candidate.DisabledReason;
        }

        public void SetSelected(bool isSelected)
        {
            selectedFrame.SetActive(isSelected);
        }
    }
}
