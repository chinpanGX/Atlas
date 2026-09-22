pub struct PlayerPachimon {
    pub player_pachimon_id: String,
    pub player_id: String,
    pub pachimon_id: i64,
    pub obtained_at: chrono::NaiveDateTime,
}

/// `player_pachimon_moves`の1行分(覚えている技)。割当自体を`player_pachimon_move_id`(ULID)で
/// 一意に参照できる(他テーブルと同様の方針、Shared/docs/design/outgame.md参照)。
#[derive(Debug, Clone)]
pub struct PlayerPachimonMove {
    pub player_pachimon_move_id: String,
    pub slot: i32,
    pub move_id: i64,
}

/// 所持パチモン一覧(`GET /players/me/pachimon`)用の集約構造体。パーティ編成状況は
/// `player_party_slots`テーブル(`PlayerPartySlot`)側で別途持つため含まない。
pub struct OwnedPachimon {
    pub player_pachimon_id: String,
    pub pachimon_id: i64,
    pub moves: Vec<PlayerPachimonMove>,
}
