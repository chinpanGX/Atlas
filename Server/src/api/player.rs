use axum::{Json, extract::State};
use serde::{Deserialize, Serialize};

use crate::error::AppError;
use crate::extractor::AuthenticatedDevice;
use crate::service::player_service;
use crate::state::AppState;

/// プレイヤー作成APIのリクエストボディ。
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreatePlayerRequest {
    pub nickname: String,
}

/// プレイヤー情報のレスポンスボディ。
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerResponse {
    pub player_id: String,
    pub nickname: String,
    pub gems: i32,
}

/// プレイヤーを作成し、認証済みデバイスに紐付けるAPIハンドラ。
///
/// 1つの`device_id`につきプレイヤーは1件のみで、既に作成済みの場合は`409`を返す。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、既にプレイヤーが存在する場合に
/// `AppError::Conflict`を返す。
pub async fn create_player_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Json(req): Json<CreatePlayerRequest>,
) -> Result<Json<PlayerResponse>, AppError> {
    let player = player_service::create(&state.pool, &device.device_id, &req.nickname).await?;

    Ok(Json(PlayerResponse {
        player_id: player.player_id,
        nickname: player.nickname,
        gems: player.gems,
    }))
}

/// 認証済みデバイスに紐づく自分のプレイヤー情報を取得するAPIハンドラ。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に
/// `AppError::NotFound`を返す。
pub async fn get_me_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
) -> Result<Json<PlayerResponse>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    Ok(Json(PlayerResponse {
        player_id: player.player_id,
        nickname: player.nickname,
        gems: player.gems,
    }))
}
