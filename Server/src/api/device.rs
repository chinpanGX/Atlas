use axum::{Json, extract::State};
use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

use crate::error::AppError;
use crate::service::{auth_service, device_service};
use crate::state::AppState;

/// デバイス登録APIのリクエストボディ。
///
/// # Fields
/// * `secret_key` - クライアント側で生成されたランダムな秘密鍵(平文)
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct RegisterDeviceRequest {
    pub secret_key: String,
}

/// デバイス登録APIのレスポンスボディ。
///
/// # Fields
/// * `device_id` - 新規に発行されたデバイスID
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct RegisterDeviceResponse {
    pub device_id: String,
}

/// 新しいデバイスを登録するAPIハンドラ。
///
/// リクエストボディの`secret_key`を元にデバイスを登録し、
/// 発行された`device_id`をレスポンスとして返す。
///
/// # Arguments
/// * `state` - アプリケーション共有状態(DBコネクションプールなどを含む)
/// * `req` - リクエストボディ(`secret_key`を含む)
///
/// # Returns
/// 登録に成功した場合、`device_id`を含む`RegisterDeviceResponse`を返す。
///
/// # Errors
/// デバイス登録処理(`device_service::register`)が失敗した場合に`AppError`を返す。
#[utoipa::path(
    post,
    path = "/devices",
    request_body = RegisterDeviceRequest,
    responses(
        (status = 200, description = "デバイス登録成功", body = RegisterDeviceResponse),
    ),
    tag = "device",
)]
pub async fn register_device_handler(
    State(state): State<AppState>,
    Json(req): Json<RegisterDeviceRequest>,
) -> Result<Json<RegisterDeviceResponse>, AppError> {
    let device = device_service::register(&state.pool, &req.secret_key).await?;

    Ok(Json(RegisterDeviceResponse {
        device_id: device.device_id,
    }))
}

/// デバイス認証APIのリクエストボディ。
///
/// # Fields
/// * `device_id` - 認証対象のデバイスID
/// * `secret_key` - デバイス登録時に指定した秘密鍵(平文)
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct AuthenticateDeviceRequest {
    pub device_id: String,
    pub secret_key: String,
}

/// デバイス認証APIのレスポンスボディ。
///
/// # Fields
/// * `access_token` - 発行されたアクセストークン
/// * `expires_in` - アクセストークンの有効期間(秒)
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct AuthenticateDeviceResponse {
    pub access_token: String,
    pub expires_in: i64,
}

/// デバイスを認証し、アクセストークンを発行するAPIハンドラ。
///
/// `device_id` + `secret_key`が一致すればアクセストークンを新規発行する。
/// 1つの`device_id`につき有効なトークンは常に1つのみで、再認証時は
/// 古いトークンを上書き(無効化)する。
///
/// # Arguments
/// * `state` - アプリケーション共有状態(DBコネクションプールなどを含む)
/// * `req` - リクエストボディ(`device_id`・`secret_key`を含む)
///
/// # Returns
/// 認証に成功した場合、`access_token`・`expires_in`を含む
/// `AuthenticateDeviceResponse`を返す。
///
/// # Errors
/// `device_id`が存在しない、または`secret_key`が一致しない場合に
/// `AppError::Unauthorized`を返す。
#[utoipa::path(
    post,
    path = "/devices/authenticate",
    request_body = AuthenticateDeviceRequest,
    responses(
        (status = 200, description = "認証成功", body = AuthenticateDeviceResponse),
        (status = 401, description = "device_idまたはsecret_keyが不正"),
    ),
    tag = "device",
)]
pub async fn authenticate_device_handler(
    State(state): State<AppState>,
    Json(req): Json<AuthenticateDeviceRequest>,
) -> Result<Json<AuthenticateDeviceResponse>, AppError> {
    let (access_token, expires_in) =
        auth_service::authenticate(&state.pool, &req.device_id, &req.secret_key).await?;

    Ok(Json(AuthenticateDeviceResponse {
        access_token,
        expires_in,
    }))
}
