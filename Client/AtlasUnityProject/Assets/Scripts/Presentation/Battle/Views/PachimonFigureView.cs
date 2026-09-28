using System.Threading;
using Atlas.Presentation.Common;
using Cysharp.Threading.Tasks;
using LitMotion;
using Supplement.Loader.Abstractions;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Atlas.Presentation.Battle
{
    // 場に出ているパチモンの姿(サムネイル)。攻撃を受けたときの揺れ・点滅と、登場・瀕死の出入りを見せる。
    // サムネイルが読み込めるまで(相手の未公開のパチモンも)は、prefabに置いた円を主タイプの色で出す。
    // 色を切り替えるため、ImageにはPaletteColorを付けず、このコンポーネントが色を決める。
    public sealed class PachimonFigureView : MonoBehaviour
    {
        private const float AppearSeconds = 0.3f;
        private const float FaintSeconds = 0.4f;
        private const float FaintDrop = 80f;
        private const float HitSeconds = 0.45f;
        private const int HitBlinkCount = 3;
        private const float HitBlinkAlpha = 0.2f;

        [SerializeField] private Image image;
        [SerializeField] private PachimonTypePalette typePalette;

        private RectTransform rectTransform;
        private Vector2 homePosition;
        private Sprite placeholderSprite;
        private PachimonThumbnailLoader thumbnailLoader;
        private long? shownPachimonId;

        public RectTransform RectTransform => rectTransform;
        public Vector2 HomePosition => homePosition;

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            homePosition = rectTransform.anchoredPosition;
            placeholderSprite = image.sprite;
            SetAlpha(0f);
        }

        [Inject]
        public void Construct(IAssetLoader assetLoader)
        {
            thumbnailLoader = new PachimonThumbnailLoader(assetLoader);
        }

        private void OnDestroy()
        {
            thumbnailLoader?.Dispose();
        }

        // 動きを付けずにすぐ反映する。瀕死なら消しておく。
        public void Refresh(PachimonFigureDto dto)
        {
            SetFigure(dto);
            ResetPose();
            SetAlpha(dto.IsFainted ? 0f : 1f);
        }

        // 繰り出されたパチモンを、小さい状態から弾むように大きくして見せる。
        public UniTask PlayAppearAsync(PachimonFigureDto dto, CancellationToken cancellation)
        {
            SetFigure(dto);
            ResetPose();
            SetAlpha(1f);
            return LMotion.Create(Vector3.zero, Vector3.one, AppearSeconds)
                .WithEase(Ease.OutBack)
                .WithOnCancel(ResetPose)
                .Bind(scale => rectTransform.localScale = scale)
                .AddTo(this)
                .ToUniTask(cancellation);
        }

        // 下へ沈みながら消える。
        public UniTask PlayFaintAsync(CancellationToken cancellation)
        {
            return LMotion.Create(0f, 1f, FaintSeconds)
                .WithEase(Ease.InQuad)
                .Bind(t =>
                {
                    rectTransform.anchoredPosition = homePosition + new Vector2(0f, -FaintDrop * t);
                    SetAlpha(1f - t);
                })
                .AddTo(this)
                .ToUniTask(cancellation);
        }

        // 左右に揺れながら点滅する。amplitudeが大きいほど強く揺れる。
        public UniTask PlayHitAsync(float amplitude, CancellationToken cancellation)
        {
            var blink = LMotion.Create(0f, 1f, HitSeconds)
                .WithOnCancel(() => SetAlpha(1f))
                .Bind(t => SetAlpha(t < 1f && (int)(t * HitBlinkCount * 2) % 2 == 0 ? HitBlinkAlpha : 1f))
                .AddTo(this)
                .ToUniTask(cancellation);
            return UniTask.WhenAll(blink, ShakeMotion.PlayAsync(rectTransform, amplitude, HitSeconds, cancellation));
        }

        private void ResetPose()
        {
            rectTransform.anchoredPosition = homePosition;
            rectTransform.localScale = Vector3.one;
        }

        // 同じパチモンのままなら読み込み直さない(読み込み済みのサムネイル、または読み込み中の円のまま)。
        private void SetFigure(PachimonFigureDto dto)
        {
            if (shownPachimonId == dto.PachimonId && dto.PachimonId is not null)
            {
                return;
            }

            shownPachimonId = dto.PachimonId;
            SetSprite(placeholderSprite, typePalette.Get(dto.PrimaryType));
            if (dto.PachimonId is { } pachimonId)
            {
                thumbnailLoader.Load(pachimonId, thumbnail => SetSprite(thumbnail, Color.white));
            }
            else
            {
                thumbnailLoader.Clear();
            }
        }

        // 点滅・瀕死の演出中でも見え方が変わらないよう、アルファは今の値を保つ。
        private void SetSprite(Sprite sprite, Color color)
        {
            color.a = image.color.a;
            image.sprite = sprite;
            image.color = color;
        }

        private void SetAlpha(float alpha)
        {
            var color = image.color;
            color.a = alpha;
            image.color = color;
        }
    }
}
