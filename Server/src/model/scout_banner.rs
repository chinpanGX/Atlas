use std::collections::HashMap;

/// スカウトバナー(Shared/docs/design/scout.md参照)。
///
/// `rate_table`はレアリティ別排出率(合計1.0)。キーは`"S"/"A"/"B"/"C"`の文字列で、
/// `master::generated::Rarity`の`Serialize`/`Deserialize`(数値専用)とは別物として扱う
/// (文字列⇔`Rarity`の変換は`scout_service`側で行う)。
pub struct ScoutBanner {
    pub banner_id: String,
    pub name: String,
    pub rate_table: HashMap<String, f64>,
    pub cost_per_roll: i32,
    pub start_at: chrono::NaiveDateTime,
    pub end_at: chrono::NaiveDateTime,
}
