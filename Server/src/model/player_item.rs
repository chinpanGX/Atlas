/// プレイヤー所持アイテム(`player_items`テーブルの1行)。
///
/// 一度も手に入れていないアイテムは行が無い。使って所持数が0になっても行は消さない。
/// `quantity`は増減量ではなく所持数そのもの(絶対値)。
pub struct PlayerItem {
    pub player_id: String,
    pub item_id: i32,
    pub quantity: i32,
}
