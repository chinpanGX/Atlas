using UnityEngine;

namespace Atlas.Presentation.Party
{
    // 所持パチモン1体分の表示データ。パーティ枠(左)と所持一覧(中央)の両方で使う。
    public sealed class PachimonDto
    {
        public string PlayerPachimonId;
        public string Name;
        // サムネイル画像は未作成。nullの間はプレースホルダ(Imageの単色表示)のまま。
        public Sprite Thumbnail;
    }
}
