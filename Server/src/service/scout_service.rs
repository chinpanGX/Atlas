use chrono::{NaiveDateTime, Utc};
use rand::RngExt;
use rand::distr::Distribution;
use rand::distr::weighted::WeightedIndex;
use sqlx::types::Json;
use sqlx::{MySql, Transaction};
use ulid::Ulid;
use voltech_grpc_error::Internal;

use crate::error::AppError;
use crate::master::{MasterData, Pachimon, Rarity, ScoutBanner};
use crate::model::scout_roll::{ScoutCandidate, ScoutRoll};
use crate::service::game_log::{GameLogCollector, ItemReason};
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::{player_item_service, player_pachimon_service};

/// 1回のロールで出す候補の数。
const CANDIDATES_PER_ROLL: usize = 10;

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
    Internal(Internal),
}

impl From<AppError> for ScoutError {
    #[track_caller]
    fn from(err: AppError) -> Self {
        Self::Internal(Internal::from(err))
    }
}

impl<E> From<E> for ScoutError
where
    E: std::error::Error + Send + Sync + 'static,
{
    #[track_caller]
    fn from(err: E) -> Self {
        Self::Internal(Internal::from(err))
    }
}

/// `scout_rolls.candidates`に保存するレア度の表現。
fn rarity_label(rarity: Rarity) -> &'static str {
    match rarity {
        Rarity::S => "S",
        Rarity::A => "A",
        Rarity::B => "B",
        Rarity::C => "C",
    }
}

/// `now`(UTC)に開催中のバナーを、開催の始まりの順に返す。
pub fn list_active_banners(master: &MasterData, now: NaiveDateTime) -> Vec<&ScoutBanner> {
    master
        .scout_banners
        .iter()
        .filter(|banner| banner.is_open(now))
        .collect()
}

/// 候補を`CANDIDATES_PER_ROLL`体ロールし、ジェム(item_id=1)を消費して`scout_rolls`へ保存する。
/// 消費した後のジェムを`diff`に、消費をゲームログに記録する。呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// バナーが存在しない場合に`ScoutError::BannerNotFound`、開催期間外の場合に`ScoutError::BannerClosed`、
/// ジェムが足りない場合に`ScoutError::InsufficientGems`、DBアクセスに失敗した場合に`ScoutError::Internal`を返す。
pub async fn create_roll(
    tx: &mut Transaction<'_, MySql>,
    diff: &mut PlayerDiffCollector,
    logs: &mut GameLogCollector,
    master: &MasterData,
    player_id: &str,
    banner_id: &str,
) -> Result<ScoutRoll, ScoutError> {
    let banner = master
        .scout_banners
        .iter()
        .find(|banner| banner.banner_id == banner_id)
        .ok_or(ScoutError::BannerNotFound)?;
    if !banner.is_open(Utc::now().naive_utc()) {
        return Err(ScoutError::BannerClosed);
    }

    let remaining_gems = player_item_service::deduct(
        tx,
        logs,
        player_id,
        player_item_service::GEM_ITEM_ID,
        banner.cost_per_roll,
        ItemReason::ScoutRoll,
    )
    .await?
    .ok_or(ScoutError::InsufficientGems)?;

    let candidates = roll_candidates(master, banner)?;

    let roll_id = Ulid::generate().to_string();
    sqlx::query(
        "INSERT INTO scout_rolls (roll_id, player_id, banner_id, candidates) VALUES (?, ?, ?, ?)",
    )
    .bind(&roll_id)
    .bind(player_id)
    .bind(banner_id)
    .bind(Json(&candidates))
    .execute(&mut **tx)
    .await?;

    diff.record_item(remaining_gems);

    Ok(ScoutRoll {
        roll_id,
        player_id: player_id.to_string(),
        banner_id: banner_id.to_string(),
        candidates,
        selected_index: None,
    })
}

/// 候補を`CANDIDATES_PER_ROLL`体、独立に抽選する(バナーの排出率の重みでレア度を決め、そのレア度の種族から等確率で選ぶ)。
fn roll_candidates(
    master: &MasterData,
    banner: &ScoutBanner,
) -> Result<Vec<ScoutCandidate>, ScoutError> {
    let mut rng = rand::rng();

    let dist = WeightedIndex::new(banner.rates.iter().map(|rate| rate.weight))?;

    let mut candidates = Vec::with_capacity(CANDIDATES_PER_ROLL);
    for _ in 0..CANDIDATES_PER_ROLL {
        let rarity = banner.rates[dist.sample(&mut rng)].rarity;

        let pool: Vec<&Pachimon> = master
            .pachimon
            .iter()
            .filter(|p| p.rarity == rarity)
            .collect();
        if pool.is_empty() {
            return Err(ScoutError::Internal(Internal::msg(format!(
                "banner {} rolled {rarity:?}, but no pachimon has that rarity",
                banner.banner_id
            ))));
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
            rarity: rarity_label(rarity).to_string(),
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
    .await?;

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
    .await?;

    let (player_pachimon, moves) =
        player_pachimon_service::grant(tx, player_id, candidate.pachimon_id, &candidate.moves)
            .await?;

    diff.record_pachimon(player_pachimon);
    for pachimon_move in moves {
        diff.record_pachimon_move(pachimon_move);
    }
    Ok(())
}
