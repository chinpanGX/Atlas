use sqlx::MySqlPool;
use ulid::Ulid;

use crate::error::AppError;
use crate::master::MasterData;
use crate::service::game_log::GameLogCollector;
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::player_pachimon_service;

/// ボットの人数。この人数に届くまでは対戦のたびに新しく作り、届いたら作ったボットから選ぶ
/// (毎回作ると、対戦のたびにプレイヤーと所持データの行が増えていくため)。
const BOT_PLAYER_COUNT: i64 = 10;

const BOT_NICKNAME: &str = "ボット";

/// 対戦相手にするボットの`player_id`を返す。
///
/// # Errors
/// DBアクセス・ボットの作成に失敗した場合に`AppError::InternalError`を返す。
pub async fn pick(pool: &MySqlPool, master: &MasterData) -> Result<String, AppError> {
    let (count,): (i64,) = sqlx::query_as("SELECT COUNT(*) FROM bot_players")
        .fetch_one(pool)
        .await?;
    if count < BOT_PLAYER_COUNT {
        return create(pool, master).await;
    }

    let (player_id,): (String,) =
        sqlx::query_as("SELECT player_id FROM bot_players ORDER BY RAND() LIMIT 1")
            .fetch_one(pool)
            .await?;
    Ok(player_id)
}

/// ボットのプレイヤーを作り、パーティをマスタからランダムに組む(作った後は組み直さない)。
///
/// 所持データの差分はクライアントに返す先が無く、ゲームログもプレイヤーの行動ではないので残さない。
async fn create(pool: &MySqlPool, master: &MasterData) -> Result<String, AppError> {
    let player_id = Ulid::generate().to_string();
    let mut tx = pool.begin().await?;

    sqlx::query("INSERT INTO players (player_id, nickname) VALUES (?, ?)")
        .bind(&player_id)
        .bind(BOT_NICKNAME)
        .execute(&mut *tx)
        .await?;
    sqlx::query("INSERT INTO bot_players (player_id) VALUES (?)")
        .bind(&player_id)
        .execute(&mut *tx)
        .await?;

    let mut diff = PlayerDiffCollector::default();
    let mut logs = GameLogCollector::default();
    player_pachimon_service::randomize_party(&mut tx, &mut diff, &mut logs, master, &player_id)
        .await?;

    tx.commit().await?;
    tracing::info!(player_id, "bot player created");
    Ok(player_id)
}
