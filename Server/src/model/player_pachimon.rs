use serde::{Deserialize, Serialize};

/// 個体値(Shared/docs/design/scout.mdの候補レスポンス、outgame.mdの`player_pachimon.ivs`と
/// 同じ形)。JSON列(`player_pachimon.ivs`、`scout_rolls.candidates[].ivs`)として保存する。
#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
pub struct Ivs {
    pub hp: i32,
    pub atk: i32,
    pub def: i32,
    pub spatk: i32,
    pub spdef: i32,
    pub speed: i32,
}

pub struct PlayerPachimon {
    pub player_pachimon_id: String,
    pub player_id: String,
    pub pachimon_id: i64,
    pub ivs: Ivs,
    pub party_slot: Option<i32>,
    pub obtained_at: chrono::NaiveDateTime,
}
