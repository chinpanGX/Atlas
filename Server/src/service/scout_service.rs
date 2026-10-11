use std::collections::HashMap;

use chrono::Utc;
use rand::RngExt;
use rand::distr::Distribution;
use rand::distr::weighted::WeightedIndex;
use sqlx::types::Json;
use sqlx::{MySql, MySqlExecutor, Transaction};
use ulid::Ulid;

use crate::error::AppError;
use crate::master::{MasterData, Pachimon, Rarity};
use crate::model::scout_banner::ScoutBanner;
use crate::model::scout_roll::{ScoutCandidate, ScoutRoll};
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::{player_item_service, player_pachimon_service};

/// スカウトのロール・候補の選択の失敗。
#[derive(Debug)]
pub enum ScoutError {
    /// バナーが無い。
    BannerNotFound,
    /// バナーが開催期間外。
    BannerClosed,
    /// ジェムが足りない。
    InsufficientGems,
    /// ロールが無い、または呼び出したプレイヤーのものでない。
    RollNotFound,
    /// 候補の番号が範囲外。
    CandidateOutOfRange,
    /// ロールの候補を選択済み。
    RollAlreadySelected,
    /// DBアクセスやマスタの不整合。
    Internal,
}

impl From<AppError> for ScoutError {
    fn from(_: AppError) -> Self {
        Self::Internal
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
pub async fn list_active_banners(
    executor: impl MySqlExecutor<'_>,
) -> Result<Vec<ScoutBanner>, AppError> {
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
    .fetch_all(executor)
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

async fn find_banner(
    executor: impl MySqlExecutor<'_>,
    banner_id: &str,
) -> Result<ScoutBanner, ScoutError> {
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
    .fetch_optional(executor)
    .await
    .map_err(|_| ScoutError::Internal)?;

    let (banner_id, name, rate_table, cost_per_roll, start_at, end_at) =
        row.ok_or(ScoutError::BannerNotFound)?;

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
/// 消費した後のジェムを`diff`に記録する。呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// バナーが存在しない場合に`ScoutError::BannerNotFound`、開催期間外の場合に`ScoutError::BannerClosed`、
/// ジェムが足りない場合に`ScoutError::InsufficientGems`、DBアクセスに失敗した場合に`ScoutError::Internal`を返す。
pub async fn create_roll(
    tx: &mut Transaction<'_, MySql>,
    diff: &mut PlayerDiffCollector,
    master: &MasterData,
    player_id: &str,
    banner_id: &str,
) -> Result<ScoutRoll, ScoutError> {
    let banner = find_banner(&mut **tx, banner_id).await?;

    let now = Utc::now().naive_utc();
    if now < banner.start_at || now > banner.end_at {
        return Err(ScoutError::BannerClosed);
    }

    let remaining_gems = player_item_service::deduct(
        tx,
        player_id,
        player_item_service::GEM_ITEM_ID,
        banner.cost_per_roll,
    )
    .await?
    .ok_or(ScoutError::InsufficientGems)?;

    let candidates = roll_candidates(master, &banner.rate_table)?;

    let roll_id = Ulid::generate().to_string();
    sqlx::query(
        "INSERT INTO scout_rolls (roll_id, player_id, banner_id, candidates) VALUES (?, ?, ?, ?)",
    )
    .bind(&roll_id)
    .bind(player_id)
    .bind(banner_id)
    .bind(Json(&candidates))
    .execute(&mut **tx)
    .await
    .map_err(|_| ScoutError::Internal)?;

    diff.record_item(remaining_gems);

    Ok(ScoutRoll {
        roll_id,
        player_id: player_id.to_string(),
        banner_id: banner_id.to_string(),
        candidates,
        selected_index: None,
    })
}

/// 候補10体を独立に抽選する(`rate_table`の重みでレアリティを決め、そのレアリティの種族から等確率で選ぶ)。
fn roll_candidates(
    master: &MasterData,
    rate_table: &HashMap<String, f64>,
) -> Result<Vec<ScoutCandidate>, ScoutError> {
    let mut rng = rand::rng();

    let labels: Vec<&str> = rate_table.keys().map(String::as_str).collect();
    let weights: Vec<f64> = labels.iter().map(|label| rate_table[*label]).collect();
    let dist = WeightedIndex::new(&weights).map_err(|_| ScoutError::Internal)?;

    let mut candidates = Vec::with_capacity(10);
    for _ in 0..10 {
        let label = labels[dist.sample(&mut rng)];
        let rarity = rarity_from_label(label).ok_or(ScoutError::Internal)?;

        let pool: Vec<&Pachimon> = master
            .pachimon
            .iter()
            .filter(|p| p.rarity == rarity)
            .collect();
        if pool.is_empty() {
            return Err(ScoutError::Internal);
        }
        let pachimon = pool[rng.random_range(0..pool.len())];

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
/// 入手した個体と技を`diff`に記録する。呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// rollが存在しない・呼び出し元のものでない場合に`ScoutError::RollNotFound`、
/// 範囲外の`index`の場合に`ScoutError::CandidateOutOfRange`、既に選択済みの場合に
/// `ScoutError::RollAlreadySelected`、DBアクセスに失敗した場合に`ScoutError::Internal`を返す。
pub async fn select_candidate(
    tx: &mut Transaction<'_, MySql>,
    diff: &mut PlayerDiffCollector,
    player_id: &str,
    roll_id: &str,
    index: i32,
) -> Result<(), ScoutError> {
    // 同じロールを同時に選んだときに二重に入手しないよう、行をロックしてから選択済みかを見る
    let row: Option<(Json<Vec<ScoutCandidate>>, Option<i32>)> = sqlx::query_as(
        "SELECT candidates, selected_index FROM scout_rolls \
         WHERE roll_id = ? AND player_id = ? FOR UPDATE",
    )
    .bind(roll_id)
    .bind(player_id)
    .fetch_optional(&mut **tx)
    .await
    .map_err(|_| ScoutError::Internal)?;

    let (Json(candidates), selected_index) = row.ok_or(ScoutError::RollNotFound)?;
    if selected_index.is_some() {
        return Err(ScoutError::RollAlreadySelected);
    }
    let candidate = usize::try_from(index)
        .ok()
        .and_then(|index| candidates.get(index))
        .ok_or(ScoutError::CandidateOutOfRange)?;

    sqlx::query(
        "UPDATE scout_rolls SET selected_index = ?, selected_at = NOW(3) \
         WHERE roll_id = ? AND player_id = ?",
    )
    .bind(index)
    .bind(roll_id)
    .bind(player_id)
    .execute(&mut **tx)
    .await
    .map_err(|_| ScoutError::Internal)?;

    let (player_pachimon, moves) =
        player_pachimon_service::grant(tx, player_id, candidate.pachimon_id, &candidate.moves)
            .await?;

    diff.record_pachimon(player_pachimon);
    for pachimon_move in moves {
        diff.record_pachimon_move(pachimon_move);
    }
    Ok(())
}
