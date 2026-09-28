namespace Atlas.Presentation.Party
{
    // 所持パチモン1体分の表示データ。パーティ枠(左)と所持一覧(中央)の両方で使う。
    public sealed class PachimonDto
    {
        public string PlayerPachimonId;
        public string Name;
        // サムネイルを引くマスタのpachimon_id。画像はViewが読み込む。
        public long PachimonId;
    }
}
