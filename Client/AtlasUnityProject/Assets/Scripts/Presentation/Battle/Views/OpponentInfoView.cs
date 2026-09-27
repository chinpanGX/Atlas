using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    public sealed class OpponentInfoView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI currentHpPercentText;
        [SerializeField] private HpGaugeView hpGauge;

        private int displayedHpPercent;

        public void Refresh(OpponentInfoDto dto)
        {
            nameText.text = dto.PachimonName;
            SetHpPercent(dto.CurrentHpPercent);
            hpGauge.SetValue(dto.CurrentHpGauge);
        }

        // HPの変化をゲージと%の両方で少しずつ見せる(名前はすぐ反映する)。
        public UniTask AnimateAsync(OpponentInfoDto dto, CancellationToken cancellation)
        {
            nameText.text = dto.PachimonName;
            var fromPercent = displayedHpPercent;
            return hpGauge.AnimateAsync(dto.CurrentHpGauge,
                progress => SetHpPercent(Mathf.RoundToInt(Mathf.Lerp(fromPercent, dto.CurrentHpPercent, progress))),
                cancellation);
        }

        private void SetHpPercent(int percent)
        {
            displayedHpPercent = percent;
            currentHpPercentText.text = $"{percent}%";
        }
    }
}
