using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    // RectTransformを左右に小刻みに揺らし、揺れを減衰させて元の位置へ戻す。
    public static class ShakeMotion
    {
        private const float Frequency = 40f;

        public static UniTask PlayAsync(RectTransform target, float amplitude, float seconds, CancellationToken cancellation)
        {
            var origin = target.anchoredPosition;
            return LMotion.Create(0f, 1f, seconds)
                .WithOnCancel(() => target.anchoredPosition = origin)
                .Bind(t => target.anchoredPosition = origin + new Vector2(amplitude * (1f - t) * Mathf.Sin(t * Frequency), 0f))
                .AddTo(target)
                .ToUniTask(cancellation);
        }
    }
}
