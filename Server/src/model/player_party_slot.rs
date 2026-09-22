/// パーティ編成1割当分(`player_party_slots`テーブルの1行)。
///
/// 割当自体をULIDで一意に識別する独立したエンティティとして扱う。割当が無いslotは
/// 行自体が存在しないため、`Option`を介した`nullable`表現を避けられる
/// (`Shared/docs/design/outgame.md`参照)。
pub struct PlayerPartySlot {
    pub party_slot_id: String,
    pub player_id: String,
    pub slot: i32,
    pub player_pachimon_id: String,
}
