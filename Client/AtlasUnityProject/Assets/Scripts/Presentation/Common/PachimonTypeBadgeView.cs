using Atlas.MasterData.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Common
{
    // タイプ名を載せた、タイプの色のバッジ。色はタイプで決まるため、背景のImageにはPaletteColorを付けない。
    public sealed class PachimonTypeBadgeView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private PachimonTypePalette typePalette;

        // Noneは単タイプのパチモンの2つ目のように、出すタイプが無いときでバッジごと隠す。
        public void Show(PachimonType type)
        {
            gameObject.SetActive(type != PachimonType.None);
            if (type == PachimonType.None)
            {
                return;
            }

            background.color = typePalette.Get(type);
            nameText.text = PachimonTypeNames.ToDisplayName(type);
        }
    }
}
