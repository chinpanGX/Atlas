using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Battle
{
    public sealed class SelfInfoView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI currentHpText;
        [SerializeField] private TextMeshProUGUI maxHpText;
        [SerializeField] private Image hpGaugeImage;

        public void Refresh(SelfInfoDto dto)
        {
            nameText.text = dto.PachimonName;
            currentHpText.text = dto.CurrentHp.ToString();
            maxHpText.text = dto.MaxHp.ToString();
            hpGaugeImage.fillAmount = dto.CurrentHpGauge;
        }
    }
}
