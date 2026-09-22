using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Battle
{
    public sealed class OpponentInfoView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI currentHpPercentText;
        [SerializeField] private Image hpGaugeImage;

        public void Refresh(OpponentInfoDto dto)
        {
            nameText.text = dto.PachimonName;
            currentHpPercentText.text = dto.CurrentHpPercent;
            hpGaugeImage.fillAmount = dto.CurrentHpGauge;
        }
    }
}
