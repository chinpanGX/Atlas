using Supplement.Loader.Abstractions;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Atlas.Presentation.Common
{
    // パチモンのサムネイル1枚。pachimon_idを受け取って自分で読み込み、読み込めるまではPlaceholderの色の単色で表示する。
    // 画像の有無で色を切り替えるため、ImageにはPaletteColorを付けず、このコンポーネントが色を決める。
    [RequireComponent(typeof(Image))]
    public sealed class PachimonThumbnailView : MonoBehaviour
    {
        [SerializeField] private Image image;
        [SerializeField] private UiPalette palette;

        private PachimonThumbnailLoader loader;
        private long? shownPachimonId;

        [Inject]
        public void Construct(IAssetLoader assetLoader)
        {
            loader = new PachimonThumbnailLoader(assetLoader);
        }

        // nullは表示するパチモンが無い(プレースホルダだけ出す)。同じIDなら読み込み直さない。
        public void Show(long? pachimonId)
        {
            if (shownPachimonId == pachimonId && pachimonId is not null)
            {
                return;
            }

            shownPachimonId = pachimonId;
            ShowPlaceholder();
            if (pachimonId is { } id)
            {
                loader.Load(id, sprite =>
                {
                    image.sprite = sprite;
                    image.color = Color.white;
                });
            }
            else
            {
                loader.Clear();
            }
        }

        private void ShowPlaceholder()
        {
            image.sprite = null;
            image.color = palette.Get(UiColorRole.Placeholder);
        }

        private void OnDestroy()
        {
            loader?.Dispose();
        }
    }
}
