use axum::{Json, extract::State};
use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

use crate::error::AppError;
use crate::extractor::AuthenticatedDevice;
use crate::service::{chat_service, player_service};
use crate::state::AppState;

/// チャット送信APIのリクエストボディ。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct SendMessageRequest {
    pub content: String,
}

/// チャット送信APIのレスポンスボディ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct SendMessageResponse {
    pub message_id: String,
    pub created_at: chrono::NaiveDateTime,
}

/// メッセージを送信するAPIハンドラ。
///
/// 認証済みデバイスに紐づくプレイヤーを送信者として、全プレイヤー共通の
/// チャットにメッセージを投稿する。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に
/// `AppError::NotFound`を返す。
#[utoipa::path(
    post,
    path = "/chat/send",
    request_body = SendMessageRequest,
    responses(
        (status = 200, description = "メッセージ送信成功", body = SendMessageResponse),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "chat",
)]
pub async fn send_message_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Json(req): Json<SendMessageRequest>,
) -> Result<Json<SendMessageResponse>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;
    let message = chat_service::send(&state.pool, &player.player_id, &req.content).await?;

    Ok(Json(SendMessageResponse {
        message_id: message.message_id,
        created_at: message.created_at,
    }))
}

/// チャットメッセージ1件分のレスポンスDTO。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct MessageDto {
    pub message_id: String,
    pub player_id: String,
    pub content: String,
    pub created_at: chrono::NaiveDateTime,
}

/// チャット受信APIのレスポンスボディ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PollMessagesResponse {
    pub messages: Vec<MessageDto>,
}

/// 全プレイヤー共通のメッセージ一覧を取得するAPIハンドラ。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`を返す。
#[utoipa::path(
    get,
    path = "/chat/poll",
    responses(
        (status = 200, description = "メッセージ一覧", body = PollMessagesResponse),
        (status = 401, description = "未認証"),
    ),
    security(("bearer_auth" = [])),
    tag = "chat",
)]
pub async fn poll_messages_handler(
    State(state): State<AppState>,
    _device: AuthenticatedDevice,
) -> Result<Json<PollMessagesResponse>, AppError> {
    let messages = chat_service::poll(&state.pool).await?;

    Ok(Json(PollMessagesResponse {
        messages: messages
            .into_iter()
            .map(|message| MessageDto {
                message_id: message.message_id,
                player_id: message.player_id,
                content: message.content,
                created_at: message.created_at,
            })
            .collect(),
    }))
}
