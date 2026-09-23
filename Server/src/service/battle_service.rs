use std::time::Duration;

use serde_json::Value;
use sqlx::{MySql, MySqlPool, QueryBuilder};
use ulid::Ulid;

use crate::error::AppError;
use crate::service::player_item_service::{self, GEM_ITEM_ID};

/// 対戦勝利報酬のgems(固定、`Shared/docs/design/battle.md`「報酬設計(gems)」参照)。
pub const BATTLE_WIN_REWARD_GEMS: i32 = 50;

/// `battle_matches.status`: 対戦中(マッチ成立〜結果報告まで)
const STATUS_IN_PROGRESS: &str = "in_progress";
/// `battle_matches.status`: 対戦終了(結果報告済み)
const STATUS_FINISHED: &str = "finished";
/// `battle_matches.status`: 勝者なしで終了(勝者なしの結果報告、または結果報告が無いまま打ち切り)
const STATUS_ABORTED: &str = "aborted";

/// 結果報告が無いまま`in_progress`で残った対戦を打ち切るまでの時間(`started_at`からの経過)。
///
/// 対戦時間に上限は無いため、これより長い正当な対戦も打ち切られ得るが、打ち切り後に届いた
/// 結果報告は受け付ける(`record_result`参照)ので、一時的に`aborted`と表示されるだけで済む。
pub const STALE_MATCH_TIMEOUT: Duration = Duration::from_secs(60 * 60);

/// `STALE_MATCH_TIMEOUT`を過ぎた対戦を探して打ち切る間隔。
const STALE_MATCH_CLEANUP_INTERVAL: Duration = Duration::from_secs(5 * 60);

/// BattleServerから報告された対戦結果。
///
/// `winner_id`が空文字の場合は勝者なし(両者とも未選出・両者とも放置等)。
/// `player1_id`/`player2_id`はBattleServer側の順番(先に接続した側がplayer1)で、
/// `battle_matches`の`player1_id`/`player2_id`とは一致するとは限らない。相手が一度も接続
/// しなかった場合、その側のIDは空文字・選出は空配列になる。
pub struct BattleResultInput {
    pub match_id: String,
    pub winner_id: String,
    pub player1_id: String,
    pub player2_id: String,
    pub player1_selected_pachimon: Vec<String>,
    pub player2_selected_pachimon: Vec<String>,
    pub turns: Vec<BattleTurnInput>,
}

/// 対戦ログ1行分(`battle_turns`の1行)。`action_data`/`result_data`はそのままJSON列に保存する。
pub struct BattleTurnInput {
    pub turn_number: i32,
    pub player_id: String,
    pub action_data: Value,
    pub result_data: Value,
}

/// マッチ成立時に`battle_matches`へ対戦行を作成する(`status = 'in_progress'`)。
///
/// # Arguments
/// * `player1_id` - 先に待機列で待っていた側
/// * `player2_id` - 後から参加してペアを成立させた側
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn create_match(
    pool: &MySqlPool,
    match_id: &str,
    player1_id: &str,
    player2_id: &str,
) -> Result<(), AppError> {
    sqlx::query(
        "INSERT INTO battle_matches (match_id, player1_id, player2_id, status, started_at) \
         VALUES (?, ?, ?, ?, NOW(3))",
    )
    .bind(match_id)
    .bind(player1_id)
    .bind(player2_id)
    .bind(STATUS_IN_PROGRESS)
    .execute(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(())
}

/// 対戦結果を記録する。`battle_matches`の終了状態への更新・`battle_turns`の一括INSERT・
/// 勝者へのgems付与を1トランザクションで行う。勝者なし(`winner_id`が空文字)の場合は
/// `aborted`にしてgemsは付与しない。
///
/// 報告の`player1_id`/`player2_id`は`battle_matches`側の参加者とIDで突き合わせ、
/// 選出を正しい列へ保存する。対象行は`FOR UPDATE`でロックするため、同時に二重報告が
/// 届いても2件目は報告済みを検出して`409`になる(gemsは二重付与されない)。
///
/// 定期処理(`abort_stale_matches`)で打ち切られた対戦に後から届いた報告は受け付け、
/// 報告内容で上書きする。報告済みかどうかは選出列で判定する(結果報告は必ず選出列を
/// 設定し、定期処理は設定しないため)。
///
/// # Errors
/// - 対戦が存在しない場合に`AppError::NotFound`
/// - `winner_id`(空文字以外)・報告の`player1_id`/`player2_id`・ターンの`player_id`が
///   参加者でない場合に`AppError::BadRequest`
/// - 既に結果報告済みの場合に`AppError::Conflict`(何も変更しない)
/// - DBアクセスに失敗した場合に`AppError::InternalError`
pub async fn record_result(pool: &MySqlPool, input: &BattleResultInput) -> Result<(), AppError> {
    let mut tx = pool.begin().await.map_err(|_| AppError::InternalError)?;

    let row: Option<(String, String, bool)> = sqlx::query_as(
        "SELECT player1_id, player2_id, player1_selected_pachimon IS NOT NULL \
         FROM battle_matches WHERE match_id = ? FOR UPDATE",
    )
    .bind(&input.match_id)
    .fetch_optional(&mut *tx)
    .await
    .map_err(|_| AppError::InternalError)?;
    let (player1_id, player2_id, already_reported) = row.ok_or(AppError::NotFound)?;

    if already_reported {
        return Err(AppError::Conflict);
    }

    let is_participant = |id: &str| id == player1_id || id == player2_id;
    let has_winner = !input.winner_id.is_empty();
    if has_winner && !is_participant(&input.winner_id) {
        return Err(AppError::BadRequest(
            "winnerId is not a participant".to_string(),
        ));
    }
    // 空文字は「一度も接続しなかった側」を表すため許容する
    for reported_id in [&input.player1_id, &input.player2_id] {
        if !reported_id.is_empty() && !is_participant(reported_id) {
            return Err(AppError::BadRequest(
                "playerId is not a participant".to_string(),
            ));
        }
    }
    if !input.player1_id.is_empty() && input.player1_id == input.player2_id {
        return Err(AppError::BadRequest("duplicate playerId".to_string()));
    }
    if input
        .turns
        .iter()
        .any(|turn| !is_participant(&turn.player_id))
    {
        return Err(AppError::BadRequest(
            "turn playerId is not a participant".to_string(),
        ));
    }

    // 報告側の順番ではなく、battle_matches側の参加者IDに対応する選出を取り出す
    let selected_of = |player_id: &str| -> &[String] {
        if input.player1_id == player_id {
            &input.player1_selected_pachimon
        } else if input.player2_id == player_id {
            &input.player2_selected_pachimon
        } else {
            &[]
        }
    };

    sqlx::query(
        "UPDATE battle_matches SET status = ?, winner_id = ?, \
         player1_selected_pachimon = ?, player2_selected_pachimon = ?, ended_at = NOW(3) \
         WHERE match_id = ?",
    )
    .bind(if has_winner { STATUS_FINISHED } else { STATUS_ABORTED })
    .bind(has_winner.then_some(&input.winner_id))
    .bind(sqlx::types::Json(selected_of(&player1_id)))
    .bind(sqlx::types::Json(selected_of(&player2_id)))
    .bind(&input.match_id)
    .execute(&mut *tx)
    .await
    .map_err(|_| AppError::InternalError)?;

    if !input.turns.is_empty() {
        let mut builder: QueryBuilder<MySql> = QueryBuilder::new(
            "INSERT INTO battle_turns \
             (turn_id, match_id, turn_number, player_id, action_data, result_data) ",
        );
        builder.push_values(&input.turns, |mut row, turn| {
            row.push_bind(Ulid::new().to_string())
                .push_bind(&input.match_id)
                .push_bind(turn.turn_number)
                .push_bind(&turn.player_id)
                .push_bind(sqlx::types::Json(&turn.action_data))
                .push_bind(sqlx::types::Json(&turn.result_data));
        });
        builder
            .build()
            .execute(&mut *tx)
            .await
            .map_err(|_| AppError::InternalError)?;
    }

    if has_winner {
        player_item_service::add(
            &mut tx,
            &input.winner_id,
            GEM_ITEM_ID,
            BATTLE_WIN_REWARD_GEMS,
        )
        .await?;
    }

    tx.commit().await.map_err(|_| AppError::InternalError)?;

    Ok(())
}

/// `started_at`から`older_than`以上経っても結果報告が無い`in_progress`の対戦を`aborted`にする。
///
/// BattleServerから結果報告が届かないケース(BattleServerの再起動・クラッシュ、両者とも
/// BattleServerに接続しなかった、結果報告の再送失敗)の後始末。打ち切った件数を返す。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn abort_stale_matches(pool: &MySqlPool, older_than: Duration) -> Result<u64, AppError> {
    let result = sqlx::query(
        "UPDATE battle_matches SET status = ?, ended_at = NOW(3) \
         WHERE status = ? AND started_at < NOW(3) - INTERVAL ? SECOND",
    )
    .bind(STATUS_ABORTED)
    .bind(STATUS_IN_PROGRESS)
    .bind(older_than.as_secs())
    .execute(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(result.rows_affected())
}

/// `abort_stale_matches`を`STALE_MATCH_CLEANUP_INTERVAL`ごとに実行するバックグラウンドタスクを起動する。
/// APIサーバー本体(`main.rs`)からのみ呼ぶ(テストでは起動しない)。
pub fn spawn_stale_match_cleanup(pool: MySqlPool) {
    tokio::spawn(async move {
        let mut interval = tokio::time::interval(STALE_MATCH_CLEANUP_INTERVAL);
        loop {
            interval.tick().await;
            match abort_stale_matches(&pool, STALE_MATCH_TIMEOUT).await {
                Ok(0) => {}
                Ok(count) => tracing::info!(count, "aborted stale battle matches"),
                Err(_) => tracing::error!("failed to abort stale battle matches"),
            }
        }
    });
}
