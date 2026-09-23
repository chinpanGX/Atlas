using System;
using UnityEngine;

namespace Atlas.Presentation.Common
{
    // ゲーム全体の配色(パーティ編成画面の配色を基準にしたもの)。各Image/テキスト/カメラは
    // PaletteColorで役割(UiColorRole)だけを持ち、実際の色はこのアセット1つで決まる。
    // Assets/Settings/UiPalette.assetを唯一のインスタンスとして使う。
    [CreateAssetMenu(fileName = "UiPalette", menuName = "Atlas/UiPalette")]
    public sealed class UiPalette : ScriptableObject
    {
        [SerializeField] private Color background = new(0.86f, 0.87f, 0.95f);
        [SerializeField] private Color panel = new(0.55f, 0.58f, 0.8f);
        [SerializeField] private Color panelLight = new(0.7f, 0.72f, 0.9f);
        [SerializeField] private Color panelDark = new(0.3f, 0.32f, 0.52f);
        [SerializeField] private Color modalPanel = new(0.55f, 0.58f, 0.8f, 0.97f);
        [SerializeField] private Color row = new(0.42f, 0.44f, 0.66f);
        [SerializeField] private Color rowLight = new(0.5f, 0.52f, 0.76f);
        [SerializeField] private Color button = Color.white;
        [SerializeField] private Color placeholder = new(0.75f, 0.75f, 0.8f);
        [SerializeField] private Color accent = new(1f, 0.84f, 0.1f);
        [SerializeField] private Color partyFrame = new(0.25f, 0.55f, 1f);
        [SerializeField] private Color statGauge = new(0.98f, 0.82f, 0.25f);
        [SerializeField] private Color hpGauge = new(0.3f, 0.8f, 0.35f);
        [SerializeField] private Color gaugeBackground = new(0.2f, 0.2f, 0.3f);
        [SerializeField] private Color textOnLight = Color.black;
        [SerializeField] private Color textOnDark = Color.white;
        [SerializeField] private Color textOnBackground = new(0.18f, 0.19f, 0.35f);
        [SerializeField] private Color textWarning = new(0.8f, 0.15f, 0.15f);

        public Color Get(UiColorRole role) => role switch
        {
            UiColorRole.Background => background,
            UiColorRole.Panel => panel,
            UiColorRole.PanelLight => panelLight,
            UiColorRole.PanelDark => panelDark,
            UiColorRole.ModalPanel => modalPanel,
            UiColorRole.Row => row,
            UiColorRole.RowLight => rowLight,
            UiColorRole.Button => button,
            UiColorRole.Placeholder => placeholder,
            UiColorRole.Accent => accent,
            UiColorRole.PartyFrame => partyFrame,
            UiColorRole.StatGauge => statGauge,
            UiColorRole.HpGauge => hpGauge,
            UiColorRole.GaugeBackground => gaugeBackground,
            UiColorRole.TextOnLight => textOnLight,
            UiColorRole.TextOnDark => textOnDark,
            UiColorRole.TextOnBackground => textOnBackground,
            UiColorRole.TextWarning => textWarning,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

#if UNITY_EDITOR
        // Inspectorで色を変えたら、開いているシーン・Prefab編集中のオブジェクトへすぐ反映する
        // (Prefabアセット自体は実行時にPaletteColor.OnEnableで反映される)。
        private void OnValidate()
        {
            foreach (var target in FindObjectsByType<PaletteColor>(FindObjectsInactive.Include))
            {
                target.Apply();
            }
        }
#endif
    }
}
