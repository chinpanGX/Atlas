/// プレイヤー所持アイテム(`player_items`テーブルの1行)。
///
/// アイテムを1つも所持しない状態は行自体が存在しないことで表現する
/// (`Shared/docs/design/outgame.md`参照)。
pub struct PlayerItem {
    pub player_id: String,
    pub item_id: i64,
    pub quantity: i32,
}
