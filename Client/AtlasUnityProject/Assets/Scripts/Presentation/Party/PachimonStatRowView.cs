using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Party
{
    public sealed class PachimonStatRowView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI labelText;
        [SerializeField] private TextMeshProUGUI valueText;
        // ゲージ本体。Image.fillAmountはスプライト未設定だと効かないため、アンカーの右端で長さを表す。
        [SerializeField] private Image gaugeFill;

        public void Refresh(PachimonStatDto stat)
        {
            labelText.text = stat.Label;
            valueText.text = stat.Value.ToString();
            gaugeFill.fillAmount = stat.Ratio;
        }
    }
}
