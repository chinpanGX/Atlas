using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    public sealed class SelfInfoView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI currentHpText;
        [SerializeField] private TextMeshProUGUI maxHpText;
        [SerializeField] private HpGaugeView hpGauge;

        private int displayedHp;

        public void Refresh(SelfInfoDto dto)
        {
            nameText.text = dto.PachimonName;
            maxHpText.text = dto.MaxHp.ToString();
            SetHp(dto.CurrentHp);
            hpGauge.SetValue(dto.CurrentHpGauge);
        }

        // HPの変化をゲージと数値の両方で少しずつ見せる(名前・最大HPはすぐ反映する)。
        public UniTask AnimateAsync(SelfInfoDto dto, CancellationToken cancellation)
        {
            nameText.text = dto.PachimonName;
            maxHpText.text = dto.MaxHp.ToString();
            var fromHp = displayedHp;
            return hpGauge.AnimateAsync(dto.CurrentHpGauge,
                progress => SetHp(Mathf.RoundToInt(Mathf.Lerp(fromHp, dto.CurrentHp, progress))),
                cancellation);
        }

        private void SetHp(int hp)
        {
            displayedHp = hp;
            currentHpText.text = hp.ToString();
        }
    }
}
