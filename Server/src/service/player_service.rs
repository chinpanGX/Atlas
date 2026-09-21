use chrono::Utc;
use sqlx::MySqlPool;
use ulid::Ulid;

use crate::error::AppError;
use crate::model::player::Player;

/// 新しいプレイヤーを作成し、デバイスに紐付ける。
///
/// 1つの`device_id`につきプレイヤーは1件のみ(`players.device_id`はUNIQUE)。
/// 既に作成済みの`device_id`で呼び出された場合はUNIQUE制約違反となる。
///
/// # Arguments
/// * `pool` - MySQLへのコネクションプール
/// * `device_id` - 認証済みデバイスID
/// * `nickname` - プレイヤー名
///
/// # Errors
/// 既にプレイヤーが作成済みの場合に`AppError::Conflict`を返す。
/// DBアクセスに失敗した場合は`AppError::InternalError`を返す。
pub async fn create(pool: &MySqlPool, device_id: &str, nickname: &str) -> Result<Player, AppError> {
    let player_id = Ulid::new().to_string();

    sqlx::query("INSERT INTO players (player_id, device_id, nickname) VALUES (?, ?, ?)")
        .bind(&player_id)
        .bind(device_id)
        .bind(nickname)
        .execute(pool)
        .await
        .map_err(|err| match &err {
            sqlx::Error::Database(db_err) if db_err.is_unique_violation() => AppError::Conflict,
            _ => AppError::InternalError,
        })?;

    Ok(Player {
        player_id,
        device_id: device_id.to_string(),
        nickname: nickname.to_string(),
        gems: 300,
        created_at: Utc::now().naive_utc(),
    })
}

/// `device_id`に紐づくプレイヤーを取得する。
///
/// # Errors
/// プレイヤーが未作成の場合に`AppError::NotFound`を返す。
/// DBアクセスに失敗した場合は`AppError::InternalError`を返す。
pub async fn find_by_device_id(pool: &MySqlPool, device_id: &str) -> Result<Player, AppError> {
    let row: Option<(String, String, String, i32, chrono::NaiveDateTime)> = sqlx::query_as(
        "SELECT player_id, device_id, nickname, gems, created_at FROM players WHERE device_id = ?",
    )
    .bind(device_id)
    .fetch_optional(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    let (player_id, device_id, nickname, gems, created_at) = row.ok_or(AppError::NotFound)?;

    Ok(Player {
        player_id,
        device_id,
        nickname,
        gems,
        created_at,
    })
}
