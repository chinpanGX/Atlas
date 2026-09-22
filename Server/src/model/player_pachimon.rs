pub struct PlayerPachimon {
    pub player_pachimon_id: String,
    pub player_id: String,
    pub pachimon_id: i64,
    pub obtained_at: chrono::NaiveDateTime,
}

/// `player_pachimon_moves`の1行分(覚えている技)。割当自体を`player_pachimon_move_id`(ULID)で
/// 一意に参照できる(他テーブルと同様の方針、Shared/docs/design/outgame.md参照)。
/// `player_pachimon_id`を持つことで、この行単体でどの個体に紐づく技かを特定できる。
#[derive(Debug, Clone)]
pub struct PlayerPachimonMove {
    pub player_pachimon_move_id: String,
    pub player_pachimon_id: String,
    pub slot: i32,
    pub move_id: i64,
}
