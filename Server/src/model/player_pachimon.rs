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
    pub obtained_at: chrono::NaiveDateTime,
}

/// `player_pachimon_moves`の1行分(覚えている技)。
#[derive(Debug, Clone, Copy)]
pub struct PlayerPachimonMove {
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
