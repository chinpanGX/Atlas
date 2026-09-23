using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    // 交代Modal左側の選出1枠分。タップで選択(詳細を中央に表示)し、交代の確定はModalの「こうたいする」で行う。
    // 場に出ている・瀕死のパチモンも詳細を見るために選択はできる(確定ボタン側で交代できないようにする)。
    public sealed class SwitchCandidateView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI hpText;
        [SerializeField] private TextMeshProUGUI statusText;
        // HPゲージ本体。スプライト未設定でも長さを変えられるよう、アンカーの右端で表す。
        [SerializeField] private RectTransform hpGaugeFill;
        [SerializeField] private GameObject selectedFrame;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void Refresh(SwitchCandidateDto candidate)
        {
            gameObject.SetActive(true);
            nameText.text = candidate.Name;
            hpText.text = $"{candidate.CurrentHp}/{candidate.MaxHp}";
            var ratio = candidate.MaxHp > 0 ? (float)candidate.CurrentHp / candidate.MaxHp : 0f;
            hpGaugeFill.anchorMax = new Vector2(Mathf.Clamp01(ratio), hpGaugeFill.anchorMax.y);
            statusText.text = candidate.IsFainted ? "ひんし" : candidate.IsActive ? "場に出ている" : string.Empty;
        }

        public void SetSelected(bool isSelected)
        {
            selectedFrame.SetActive(isSelected);
        }

        // 選出が3体未満の場合など、候補が無い枠は隠す。
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
