use axum::{Json, extract::State};
use serde::{Deserialize, Serialize};

use crate::error::AppError;
use crate::service::device_service;
use crate::state::AppState;

/// デバイス登録APIのリクエストボディ。
///
/// # Fields
/// * `secret_key` - クライアント側で生成されたランダムな秘密鍵(平文)
#[derive(Deserialize)]
pub struct RegisterDeviceRequest {
    pub secret_key: String,
}

/// デバイス登録APIのレスポンスボディ。
///
/// # Fields
/// * `device_id` - 新規に発行されたデバイスID
/// * `player_id` - 新規に発行されたプレイヤーID
#[derive(Serialize)]
pub struct RegisterDeviceResponse {
    pub device_id: String,
    pub player_id: String,
}

/// 新しいデバイスを登録するAPIハンドラ。
///
/// リクエストボディの`secret_key`を元にデバイスを登録し、
/// 発行された`device_id`・`player_id`をレスポンスとして返す。
///
/// # Arguments
/// * `state` - アプリケーション共有状態(DBコネクションプールなどを含む)
/// * `req` - リクエストボディ(`secret_key`を含む)
///
/// # Returns
/// 登録に成功した場合、`device_id`・`player_id`を含む`RegisterDeviceResponse`を返す。
///
/// # Errors
/// デバイス登録処理(`device_service::register`)が失敗した場合に`AppError`を返す。
pub async fn register_device_handler(
    State(state): State<AppState>,
    Json(req): Json<RegisterDeviceRequest>,
) -> Result<Json<RegisterDeviceResponse>, AppError> {
    let device = device_service::register(&state.pool, &req.secret_key).await?;

    Ok(Json(RegisterDeviceResponse {
        // ここは device_id: device.device_id のように名前が違うので、フィールド初期化省略記法は使えない
        device_id: device.device_id,
        player_id: device.player_id,
    }))
}
