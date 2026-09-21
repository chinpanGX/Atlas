use serde::{Deserialize, Serialize};

use super::player_pachimon::Ivs;

/// 紹介の候補1体分(Shared/docs/design/scout.md参照)。`scout_rolls.candidates`(JSON配列)の
/// 要素として保存し、そのままAPIレスポンスの候補としても使う。
///
/// `rarity`は`"S"/"A"/"B"/"C"`の文字列(`ScoutBanner.rate_table`と同じ表現)。
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScoutCandidate {
    pub pachimon_id: i64,
    pub rarity: String,
    pub ivs: Ivs,
    pub moves: Vec<i64>,
}

pub struct ScoutRoll {
    pub roll_id: String,
    pub player_id: String,
    pub banner_id: String,
    pub candidates: Vec<ScoutCandidate>,
    pub selected_index: Option<i32>,
}
