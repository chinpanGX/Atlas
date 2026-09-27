/// プレイヤー所持アイテム(`player_items`テーブルの1行)。
///
/// アイテムを1つも所持しない状態は行自体が存在しないことで表現する。
/// `quantity`は増減量ではなく所持数そのもの(絶対値)。
pub struct PlayerItem {
    pub player_id: String,
    pub item_id: i32,
    pub quantity: i32,
}
