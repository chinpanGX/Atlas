use std::collections::HashMap;

use chrono::Utc;
use rand::Rng;
use rand::distributions::{Distribution, WeightedIndex};
use sqlx::MySqlPool;
use sqlx::types::Json;
use ulid::Ulid;

use crate::error::AppError;
use crate::master::{MasterData, Pachimon, Rarity};
use crate::model::player_item::PlayerItem;
use crate::model::player_pachimon::{PlayerPachimon, PlayerPachimonMove};
use crate::model::scout_banner::ScoutBanner;
use crate::model::scout_roll::{ScoutCandidate, ScoutRoll};
use crate::service::{player_item_service, player_pachimon_service};

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

/// 候補10体をロールし、ジェム(item_id=1)を消費して`scout_rolls`へ保存する。
///
/// # Errors
/// バナーが存在しない場合`AppError::NotFound`、開催期間外・ジェム不足の場合
/// `AppError::BadRequest`、DBアクセスに失敗した場合`AppError::InternalError`を返す。
pub async fn create_roll(
    pool: &MySqlPool,
    master: &MasterData,
    player_id: &str,
    banner_id: &str,
) -> Result<(ScoutRoll, PlayerItem), AppError> {
    let banner = find_banner(pool, banner_id).await?;

    let now = Utc::now().naive_utc();
    if now < banner.start_at || now > banner.end_at {
        return Err(AppError::BadRequest(
            "scout banner is not active".to_string(),
        ));
    }

    let mut tx = pool.begin().await.map_err(|_| AppError::InternalError)?;

    let remaining_gems = player_item_service::deduct(
        &mut tx,
        player_id,
        player_item_service::GEM_ITEM_ID,
        banner.cost_per_roll,
    )
    .await?;

    let candidates = roll_candidates(master, &banner.rate_table)?;

    let roll_id = Ulid::new().to_string();
    sqlx::query(
        "INSERT INTO scout_rolls (roll_id, player_id, banner_id, candidates) VALUES (?, ?, ?, ?)",
    )
    .bind(&roll_id)
    .bind(player_id)
    .bind(banner_id)
    .bind(Json(&candidates))
    .execute(&mut *tx)
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
        remaining_gems,
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

        let pool: Vec<&Pachimon> = master
            .pachimon
            .iter()
            .filter(|p| p.rarity == rarity)
            .collect();
        if pool.is_empty() {
            return Err(AppError::InternalError);
        }
        let pachimon = pool[rng.gen_range(0..pool.len())];

        let moves: Vec<i32> = master
            .move_group_moves
            .iter()
            .filter(|row| row.group_id == pachimon.move_group_id && row.is_initial)
            .map(|row| row.move_id)
            .collect();

        candidates.push(ScoutCandidate {
            pachimon_id: pachimon.pachimon_id,
            rarity: label.to_string(),
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
) -> Result<(PlayerPachimon, Vec<PlayerPachimonMove>), AppError> {
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
    let (player_pachimon, moves) =
        player_pachimon_service::grant(&mut tx, player_id, candidate.pachimon_id, &candidate.moves)
            .await?;

    tx.commit().await.map_err(|_| AppError::InternalError)?;

    Ok((player_pachimon, moves))
}
