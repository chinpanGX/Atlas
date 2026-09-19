use chrono::Utc;
use sqlx::MySqlPool;
use ulid::Ulid;

use crate::error::AppError;
use crate::model::messages::Message;

/// `poll`で一度に返す最大件数。
const CHAT_POLL_LIMIT: i64 = 50;

/// チャットメッセージを送信する。
///
/// チャットルームの概念はなく、全プレイヤー共通の1つのチャットに投稿する。
///
/// # Arguments
/// * `pool` - MySQLへのコネクションプール
/// * `player_id` - 送信者のプレイヤーID
/// * `content` - メッセージ本文
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn send(pool: &MySqlPool, player_id: &str, content: &str) -> Result<Message, AppError> {
    let message_id = Ulid::new().to_string();

    sqlx::query("INSERT INTO messages (message_id, player_id, content) VALUES (?, ?, ?)")
        .bind(&message_id)
        .bind(player_id)
        .bind(content)
        .execute(pool)
        .await
        .map_err(|_| AppError::InternalError)?;

    Ok(Message {
        message_id,
        player_id: player_id.to_string(),
        content: content.to_string(),
        created_at: Utc::now().naive_utc(),
    })
}

/// 全プレイヤー共通のメッセージ一覧を、直近`CHAT_POLL_LIMIT`件分取得する。
///
/// `created_at`の新しい順にDBから取得したのち、表示用に古い順へ並び替えて返す。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn poll(pool: &MySqlPool) -> Result<Vec<Message>, AppError> {
    let rows: Vec<(String, String, String, chrono::NaiveDateTime)> = sqlx::query_as(
        "SELECT message_id, player_id, content, created_at FROM messages ORDER BY created_at DESC LIMIT ?",
    )
    .bind(CHAT_POLL_LIMIT)
    .fetch_all(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    let mut messages: Vec<Message> = rows
        .into_iter()
        .map(|(message_id, player_id, content, created_at)| Message {
            message_id,
            player_id,
            content,
            created_at,
        })
        .collect();
    messages.reverse();

    Ok(messages)
}
