use argon2::Argon2;
use argon2::password_hash::{PasswordHash, PasswordVerifier};
use chrono::{Duration, Utc};
use sqlx::MySqlPool;
use ulid::Ulid;

use crate::error::AppError;

/// アクセストークンの有効期間(秒)。
const ACCESS_TOKEN_TTL_SECONDS: i64 = 3600;

/// デバイスを認証し、アクセストークンを発行する。
///
/// `device_id`に紐づく`secret_key_hash`と`secret_key`を照合し、一致した場合のみ
/// 新しいアクセストークンを発行する。1つの`device_id`につき有効なトークンは常に1つ
/// のみで、再認証時は`access_tokens`をUPSERTして古いトークンを上書き(無効化)する。
///
/// # Arguments
/// * `pool` - MySQLへのコネクションプール
/// * `device_id` - 認証対象のデバイスID
/// * `secret_key` - クライアント側で保持している秘密鍵(平文)
///
/// # Returns
/// 発行された`access_token`と有効期間(秒)のタプル。
///
/// # Errors
/// `device_id`が存在しない場合、または`secret_key`が一致しない場合に
/// `AppError::Unauthorized`を返す。DBアクセスやハッシュ処理に失敗した場合は
/// `AppError::InternalError`を返す。
pub async fn authenticate(
    pool: &MySqlPool,
    device_id: &str,
    secret_key: &str,
) -> Result<(String, i64), AppError> {
    let row: Option<(String,)> =
        sqlx::query_as("SELECT secret_key_hash FROM devices WHERE device_id = ?")
            .bind(device_id)
            .fetch_optional(pool)
            .await
            .map_err(|_| AppError::InternalError)?;

    let secret_key_hash = row.ok_or(AppError::Unauthorized)?.0;

    let parsed_hash =
        PasswordHash::new(&secret_key_hash).map_err(|_| AppError::InternalError)?;
    Argon2::default()
        .verify_password(secret_key.as_bytes(), &parsed_hash)
        .map_err(|_| AppError::Unauthorized)?;

    // ULIDを2つ連結し、64文字以内で十分なランダム性を持つトークンとする
    let access_token = format!("{}{}", Ulid::new(), Ulid::new());
    let expires_at = Utc::now().naive_utc() + Duration::seconds(ACCESS_TOKEN_TTL_SECONDS);

    sqlx::query(
        "INSERT INTO access_tokens (device_id, access_token, expires_at) VALUES (?, ?, ?)
         ON DUPLICATE KEY UPDATE
             access_token = VALUES(access_token),
             expires_at = VALUES(expires_at),
             created_at = CURRENT_TIMESTAMP(3)",
    )
    .bind(device_id)
    .bind(&access_token)
    .bind(expires_at)
    .execute(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok((access_token, ACCESS_TOKEN_TTL_SECONDS))
}

/// アクセストークンを検証し、紐づく`device_id`を取り出す。
///
/// `access_tokens`に該当トークンが存在し、かつ有効期限内であれば有効とみなす。
/// `GET /auth/verify`や、要認証エンドポイントの`AuthenticatedDevice`extractorから
/// 共通で利用される。
///
/// # Arguments
/// * `pool` - MySQLへのコネクションプール
/// * `access_token` - 検証対象のアクセストークン
///
/// # Returns
/// トークンに紐づく`device_id`。
///
/// # Errors
/// トークンが存在しない、または期限切れの場合に`AppError::Unauthorized`を返す。
/// DBアクセスに失敗した場合は`AppError::InternalError`を返す。
pub async fn resolve_device_id(pool: &MySqlPool, access_token: &str) -> Result<String, AppError> {
    let row: Option<(String, chrono::NaiveDateTime)> =
        sqlx::query_as("SELECT device_id, expires_at FROM access_tokens WHERE access_token = ?")
            .bind(access_token)
            .fetch_optional(pool)
            .await
            .map_err(|_| AppError::InternalError)?;

    match row {
        Some((device_id, expires_at)) if expires_at > Utc::now().naive_utc() => Ok(device_id),
        _ => Err(AppError::Unauthorized),
    }
}
