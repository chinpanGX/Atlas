using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Common
{
    // 同じGameObjectのGraphic(Image/TextMeshProUGUI)またはCameraの背景色を、UiPaletteの役割の色にする。
    // 色を直接書かず役割だけを持たせることで、配色の変更をUiPalette 1か所で済ませる。
    // Editor上でも反映されるようExecuteAlwaysにしている。
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PaletteColor : MonoBehaviour
    {
        [SerializeField] private UiPalette palette;
        [SerializeField] private UiColorRole role;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        public void Apply()
        {
            if (palette == null)
            {
                return;
            }

            var color = palette.Get(role);
            if (TryGetComponent<Graphic>(out var graphic))
            {
                // TextMeshProUGUIではGraphic.colorが文字色(fontColor)になる。
                graphic.color = color;
            }
            else if (TryGetComponent<Camera>(out var targetCamera))
            {
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = color;
            }
        }
    }
}
