use serde::{Deserialize, Serialize};

pub struct PlayerPachimon {
    pub player_pachimon_id: String,
    pub player_id: String,
    pub pachimon_id: i32,
    pub obtained_at: chrono::NaiveDateTime,
    pub stat_points: StatPoints,
}

/// 6つの能力に割り振ったステータスポイント(`player_pachimon.effort_values`のJSONの中身)。
/// 種族値から計算した能力の値にそのまま足す。
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct StatPoints {
    pub hp: i32,
    pub atk: i32,
    pub def: i32,
    pub spatk: i32,
    pub spdef: i32,
    pub speed: i32,
}

impl StatPoints {
    /// 1体が割り振れるポイントの合計の上限。
    pub const MAX_TOTAL: i32 = 64;

    fn values(self) -> [i32; 6] {
        [
            self.hp, self.atk, self.def, self.spatk, self.spdef, self.speed,
        ]
    }

    /// どれも0以上で、合計が`MAX_TOTAL`以下か。
    pub fn is_valid(self) -> bool {
        let values = self.values();
        values.iter().all(|v| *v >= 0) && values.iter().sum::<i32>() <= Self::MAX_TOTAL
    }

    /// `self`から`after`に変えるときに増えるポイントの合計(能力ごとの`max(0, after - self)`の和)。
    /// 減らした分は差し引かない(減らすのは無料のため)。
    pub fn increase_to(self, after: StatPoints) -> i32 {
        self.values()
            .iter()
            .zip(after.values())
            .map(|(before, after)| (after - before).max(0))
            .sum()
    }
}

/// `player_pachimon_moves`の1行分(覚えている技)。割当自体を`player_pachimon_move_id`(ULID)で
/// 一意に参照できる(`playerDiff`の`removed`でIDを指せるようにするため、他テーブルと同様の方針)。
/// `player_pachimon_id`を持つことで、この行単体でどの個体に紐づく技かを特定できる。
#[derive(Debug, Clone)]
pub struct PlayerPachimonMove {
    pub player_pachimon_move_id: String,
    pub player_pachimon_id: String,
    pub slot: i32,
    pub move_id: i32,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn points(hp: i32, atk: i32, speed: i32) -> StatPoints {
        StatPoints {
            hp,
            atk,
            speed,
            ..StatPoints::default()
        }
    }

    /// 合計64までは有効で、65以上や負の値は無効。1つの能力に64を全部入れてもよい。
    #[test]
    fn validates_total_and_sign() {
        assert!(StatPoints::default().is_valid());
        assert!(points(64, 0, 0).is_valid());
        assert!(points(32, 16, 16).is_valid());
        assert!(!points(32, 16, 17).is_valid());
        assert!(!points(-1, 0, 0).is_valid());
    }

    /// 増やした分だけを数え、減らした分は差し引かない。
    #[test]
    fn counts_only_increases() {
        assert_eq!(StatPoints::default().increase_to(points(64, 0, 0)), 64);
        // こうげきから10外してすばやさに10足すと10
        assert_eq!(points(0, 30, 0).increase_to(points(0, 20, 10)), 10);
        assert_eq!(points(10, 10, 10).increase_to(points(0, 0, 0)), 0);
        assert_eq!(points(10, 10, 10).increase_to(points(10, 10, 10)), 0);
    }
}
