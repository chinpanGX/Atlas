use std::collections::HashMap;

use chrono::Utc;
use rand::Rng;
use rand::distributions::{Distribution, WeightedIndex};
use sqlx::MySqlPool;
use sqlx::types::Json;
use ulid::Ulid;

use crate::error::AppError;
use crate::master::{MasterData, Pachimon, Rarity};
use crate::model::player_pachimon::{Ivs, PlayerPachimon};
use crate::model::scout_banner::ScoutBanner;
use crate::model::scout_roll::{ScoutCandidate, ScoutRoll};

/// `Rarity`をAPI/`rate_table`上の文字列表現に変換する。
///
/// 生成済み`Rarity`の`Serialize`/`Deserialize`(`master/generated/rarity.rs`)は数値専用
/// (master-data-pipelineのDB保存形式に合わせたもの)なので、スカウトのレスポンス・
/// `scout_banners.rate_table`で使う`"S"/"A"/"B"/"C"`表現とは別にこの変換を持つ。
pub fn rarity_to_label(rarity: Rarity) -> &'static str {
    match rarity {
        Rarity::S => "S",
        Rarity::A => "A",
        Rarity::B => "B",
        Rarity::C => "C",
    }
}

fn rarity_from_label(label: &str) -> Option<Rarity> {
    match label {
        "S" => Some(Rarity::S),
        "A" => Some(Rarity::A),
        "B" => Some(Rarity::B),
        "C" => Some(Rarity::C),
        _ => None,
    }
}

/// 開催中のスカウトバナー一覧を取得する。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn list_active_banners(pool: &MySqlPool) -> Result<Vec<ScoutBanner>, AppError> {
    let rows: Vec<(
        String,
        String,
        Json<HashMap<String, f64>>,
        i32,
        chrono::NaiveDateTime,
        chrono::NaiveDateTime,
    )> = sqlx::query_as(
        "SELECT banner_id, name, rate_table, cost_per_roll, start_at, end_at \
         FROM scout_banners WHERE start_at <= NOW(3) AND end_at >= NOW(3) ORDER BY start_at",
    )
    .fetch_all(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(rows
        .into_iter()
        .map(
            |(banner_id, name, rate_table, cost_per_roll, start_at, end_at)| ScoutBanner {
                banner_id,
                name,
                rate_table: rate_table.0,
                cost_per_roll,
                start_at,
                end_at,
            },
        )
        .collect())
}

async fn find_banner(pool: &MySqlPool, banner_id: &str) -> Result<ScoutBanner, AppError> {
    let row: Option<(
        String,
        String,
        Json<HashMap<String, f64>>,
        i32,
        chrono::NaiveDateTime,
        chrono::NaiveDateTime,
    )> = sqlx::query_as(
        "SELECT banner_id, name, rate_table, cost_per_roll, start_at, end_at \
         FROM scout_banners WHERE banner_id = ?",
    )
    .bind(banner_id)
    .fetch_optional(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    let (banner_id, name, rate_table, cost_per_roll, start_at, end_at) =
        row.ok_or(AppError::NotFound)?;

    Ok(ScoutBanner {
        banner_id,
        name,
        rate_table: rate_table.0,
        cost_per_roll,
        start_at,
        end_at,
    })
}

/// 候補10体をロールし、gemsを消費して`scout_rolls`へ保存する。
///
/// # Errors
/// バナーが存在しない場合`AppError::NotFound`、開催期間外・gems不足の場合
/// `AppError::BadRequest`、DBアクセスに失敗した場合`AppError::InternalError`を返す。
pub async fn create_roll(
    pool: &MySqlPool,
    master: &MasterData,
    player_id: &str,
    banner_id: &str,
) -> Result<(ScoutRoll, i32), AppError> {
    let banner = find_banner(pool, banner_id).await?;

    let now = Utc::now().naive_utc();
    if now < banner.start_at || now > banner.end_at {
        return Err(AppError::BadRequest("scout banner is not active".to_string()));
    }

    let mut tx = pool.begin().await.map_err(|_| AppError::InternalError)?;

    let update_result = sqlx::query("UPDATE players SET gems = gems - ? WHERE player_id = ? AND gems >= ?")
        .bind(banner.cost_per_roll)
        .bind(player_id)
        .bind(banner.cost_per_roll)
        .execute(&mut *tx)
        .await
        .map_err(|_| AppError::InternalError)?;

    if update_result.rows_affected() == 0 {
        return Err(AppError::BadRequest("insufficient gems".to_string()));
    }

    let candidates = roll_candidates(master, &banner.rate_table)?;

    let roll_id = Ulid::new().to_string();
    sqlx::query("INSERT INTO scout_rolls (roll_id, player_id, banner_id, candidates) VALUES (?, ?, ?, ?)")
        .bind(&roll_id)
        .bind(player_id)
        .bind(banner_id)
        .bind(Json(&candidates))
        .execute(&mut *tx)
        .await
        .map_err(|_| AppError::InternalError)?;

    let remaining_gems: (i32,) = sqlx::query_as("SELECT gems FROM players WHERE player_id = ?")
        .bind(player_id)
        .fetch_one(&mut *tx)
        .await
        .map_err(|_| AppError::InternalError)?;

    tx.commit().await.map_err(|_| AppError::InternalError)?;

    Ok((
        ScoutRoll {
            roll_id,
            player_id: player_id.to_string(),
            banner_id: banner_id.to_string(),
            candidates,
            selected_index: None,
        },
        remaining_gems.0,
    ))
}

/// 候補10体を独立に抽選する(`Shared/docs/design/scout.md`の「抽選ロジック」参照)。
fn roll_candidates(
    master: &MasterData,
    rate_table: &HashMap<String, f64>,
) -> Result<Vec<ScoutCandidate>, AppError> {
    let mut rng = rand::thread_rng();

    let labels: Vec<&str> = rate_table.keys().map(String::as_str).collect();
    let weights: Vec<f64> = labels.iter().map(|label| rate_table[*label]).collect();
    let dist = WeightedIndex::new(&weights).map_err(|_| AppError::InternalError)?;

    let mut candidates = Vec::with_capacity(10);
    for _ in 0..10 {
        let label = labels[dist.sample(&mut rng)];
        let rarity = rarity_from_label(label).ok_or(AppError::InternalError)?;

        let pool: Vec<&Pachimon> = master.pachimon.iter().filter(|p| p.rarity == rarity).collect();
        if pool.is_empty() {
            return Err(AppError::InternalError);
        }
        let pachimon = pool[rng.gen_range(0..pool.len())];

        let ivs = Ivs {
            hp: rng.gen_range(0..=31),
            atk: rng.gen_range(0..=31),
            def: rng.gen_range(0..=31),
            spatk: rng.gen_range(0..=31),
            spdef: rng.gen_range(0..=31),
            speed: rng.gen_range(0..=31),
        };

        let moves: Vec<i64> = master
            .move_group_moves
            .iter()
            .filter(|row| row.group_id == pachimon.move_group_id && row.is_initial)
            .map(|row| row.move_id)
            .collect();

        candidates.push(ScoutCandidate {
            pachimon_id: pachimon.pachimon_id,
            rarity: label.to_string(),
            ivs,
            moves,
        });
    }

    Ok(candidates)
}

/// 保存済みの候補から1体を選んで`player_pachimon`/`player_pachimon_moves`へコピーする。
///
/// # Errors
/// rollが存在しない・呼び出し元のものでない場合に`AppError::NotFound`、
/// 範囲外の`index`の場合に`AppError::BadRequest`、既に選択済みの場合に`AppError::Conflict`、
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn select_candidate(
    pool: &MySqlPool,
    player_id: &str,
    roll_id: &str,
    index: i32,
) -> Result<PlayerPachimon, AppError> {
    let mut tx = pool.begin().await.map_err(|_| AppError::InternalError)?;

    let row: Option<(Json<Vec<ScoutCandidate>>,)> = sqlx::query_as(
        "SELECT candidates FROM scout_rolls WHERE roll_id = ? AND player_id = ? FOR UPDATE",
    )
    .bind(roll_id)
    .bind(player_id)
    .fetch_optional(&mut *tx)
    .await
    .map_err(|_| AppError::InternalError)?;

    let candidates = row.ok_or(AppError::NotFound)?.0.0;

    if index < 0 || index as usize >= candidates.len() {
        return Err(AppError::BadRequest("index out of range".to_string()));
    }

    let update_result = sqlx::query(
        "UPDATE scout_rolls SET selected_index = ?, selected_at = NOW(3) \
         WHERE roll_id = ? AND player_id = ? AND selected_index IS NULL",
    )
    .bind(index)
    .bind(roll_id)
    .bind(player_id)
    .execute(&mut *tx)
    .await
    .map_err(|_| AppError::InternalError)?;

    if update_result.rows_affected() == 0 {
        return Err(AppError::Conflict);
    }

    let candidate = &candidates[index as usize];
    let player_pachimon_id = Ulid::new().to_string();
    let effort_values = serde_json::json!({"hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 0});

    sqlx::query(
        "INSERT INTO player_pachimon (player_pachimon_id, player_id, pachimon_id, ivs, effort_values) \
         VALUES (?, ?, ?, ?, ?)",
    )
    .bind(&player_pachimon_id)
    .bind(player_id)
    .bind(candidate.pachimon_id)
    .bind(Json(&candidate.ivs))
    .bind(Json(&effort_values))
    .execute(&mut *tx)
    .await
    .map_err(|_| AppError::InternalError)?;

    for (slot, move_id) in candidate.moves.iter().enumerate() {
        sqlx::query("INSERT INTO player_pachimon_moves (player_pachimon_id, slot, move_id) VALUES (?, ?, ?)")
            .bind(&player_pachimon_id)
            .bind(slot as i32 + 1)
            .bind(*move_id)
            .execute(&mut *tx)
            .await
            .map_err(|_| AppError::InternalError)?;
    }

    let obtained_at: (chrono::NaiveDateTime,) =
        sqlx::query_as("SELECT obtained_at FROM player_pachimon WHERE player_pachimon_id = ?")
            .bind(&player_pachimon_id)
            .fetch_one(&mut *tx)
            .await
            .map_err(|_| AppError::InternalError)?;

    let ivs = candidate.ivs;
    let pachimon_id = candidate.pachimon_id;

    tx.commit().await.map_err(|_| AppError::InternalError)?;

    Ok(PlayerPachimon {
        player_pachimon_id,
        player_id: player_id.to_string(),
        pachimon_id,
        ivs,
        party_slot: None,
        obtained_at: obtained_at.0,
    })
}
