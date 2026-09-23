namespace Atlas.Presentation.Common
{
    // UiPaletteで色を引くための役割。Prefab/シーンにはPaletteColorのroleとして整数で保存されるため、
    // 並び順を変えたり途中に挿入したりしない(追加は末尾に行う)。
    public enum UiColorRole
    {
        // 画面全体の背景(カメラの背景色)。
        Background,
        // 一覧・パーティ枠などの中間色のパネル。
        Panel,
        // 所持パチモン一覧のような、Panelより明るいパネル。
        PanelLight,
        // 詳細パネル・バトルのHUDなど、白文字を載せる濃いパネル。
        PanelDark,
        // Modalの背景パネル。
        ModalPanel,
        // 詳細パネル内のステータス行。
        Row,
        // 詳細パネル内の技の行。
        RowLight,
        // ボタン・セルの地の色。
        Button,
        // サムネイル未設定時のプレースホルダ。
        Placeholder,
        // 選択中の枠。
        Accent,
        // 編成中の枠。
        PartyFrame,
        // ステータスのゲージ。
        StatGauge,
        // HPゲージ(残りHPの意味を持つ色のため緑)。
        HpGauge,
        // ゲージの下地。
        GaugeBackground,
        // 明るい地(ボタン・HPゲージ)の上の文字。
        TextOnLight,
        // 濃い地(パネル・Modal)の上の文字。
        TextOnDark,
        // 画面の背景に直接載せる文字。
        TextOnBackground,
        // 「ひんし」など注意を引く文字。
        TextWarning,
    }
}
