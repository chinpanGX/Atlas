using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    public sealed class SwitchCandidateView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI hpText;
        [SerializeField] private TextMeshProUGUI statusText;
        // HPゲージ本体。スプライト未設定でも長さを変えられるよう、アンカーの右端で表す。
        [SerializeField] private RectTransform hpGaugeFill;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void Refresh(SwitchCandidateDto candidate)
        {
            gameObject.SetActive(true);
            nameText.text = candidate.Name;
            hpText.text = $"{candidate.HpPercent}%";
            hpGaugeFill.anchorMax = new Vector2(candidate.HpPercent / 100f, hpGaugeFill.anchorMax.y);
            statusText.text = candidate.IsFainted ? "ひんし" : candidate.IsActive ? "場に出ている" : string.Empty;
            button.interactable = !candidate.IsFainted && !candidate.IsActive;
        }

        // 選出が3体未満の場合など、候補が無い枠は隠す。
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
