using System;
using System.Threading;
using Atlas.Presentation.Common;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Battle
{
    // HPゲージ。残りHPに応じて色を変え(半分より多い=HpGauge、半分以下=HpGaugeMiddle、2割以下=HpGaugeLow)、
    // HPが変わったときは少しずつ減らして(増やして)見せる。
    // 色を実行時に切り替えるため、ゲージのImageにはPaletteColorを付けず、このコンポーネントが色を決める。
    public sealed class HpGaugeView : MonoBehaviour
    {
        private const float MiddleThreshold = 0.5f;
        private const float LowThreshold = 0.2f;
        // 差が小さくても減ったことが分かる最短の長さ。
        private const float MinSeconds = 0.25f;

        [SerializeField] private Image gaugeImage;
        [SerializeField] private UiPalette palette;
        // ゲージ全体(0→1)を動かすのにかかる秒数。減った量に比例した長さで動かす。
        [SerializeField] private float secondsPerFullGauge = 1f;

        public float Value => gaugeImage.fillAmount;

        public void SetValue(float value)
        {
            gaugeImage.fillAmount = value;
            gaugeImage.color = palette.Get(RoleOf(value));
        }

        // 今の値からvalueまで動かし、動き終わるまで待つ。onProgressには進み具合(0〜1)を渡す
        // (ゲージと一緒にHPの数値も動かすため)。
        public UniTask AnimateAsync(float value, Action<float> onProgress, CancellationToken cancellation)
        {
            var from = Value;
            if (Mathf.Approximately(from, value))
            {
                SetValue(value);
                onProgress?.Invoke(1f);
                return UniTask.CompletedTask;
            }

            var seconds = Mathf.Max(MinSeconds, Mathf.Abs(value - from) * secondsPerFullGauge);
            return LMotion.Create(0f, 1f, seconds)
                .WithEase(Ease.OutQuad)
                .Bind(progress =>
                {
                    SetValue(Mathf.Lerp(from, value, progress));
                    onProgress?.Invoke(progress);
                })
                .AddTo(this)
                .ToUniTask(cancellation);
        }

        private static UiColorRole RoleOf(float value)
        {
            if (value <= LowThreshold)
            {
                return UiColorRole.HpGaugeLow;
            }

            return value <= MiddleThreshold ? UiColorRole.HpGaugeMiddle : UiColorRole.HpGauge;
        }
    }
}
