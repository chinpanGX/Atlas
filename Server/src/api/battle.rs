use axum::{Json, extract::State};
use serde::Serialize;
use utoipa::ToSchema;

use crate::error::AppError;
use crate::extractor::AuthenticatedDevice;
use crate::service::{matchmaking_service, player_pachimon_service, player_service};
use crate::state::AppState;

/// `QueueStatusResponse::status`: まだ待機中(マッチ未成立)
const STATUS_WAITING: &str = "waiting";
/// `QueueStatusResponse::status`: マッチ成立
const STATUS_MATCHED: &str = "matched";

/// マッチ成立確認APIのレスポンスボディ。
///
/// api-codegenがnullableに対応しないため`Option`は使わず、`status`が`"waiting"`の間は
/// `matchId`/`battleServer`/`battleToken`を空文字で返す。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct QueueStatusResponse {
    /// `"waiting"` or `"matched"`
    pub status: String,
    pub match_id: String,
    pub battle_server: String,
    /// BattleServer接続用の短命JWT(HS256, claims: `match_id`/`player_id`, 有効期限30秒)
    pub battle_token: String,
}

/// マッチング待機列に参加するAPIハンドラ。
///
/// 他に待機者がいればその場でペアを成立させる。成立結果は`GET /battle/queue/status`で取得する。
/// 既に待機中・マッチ成立済み(結果未取得)の場合は何もしない。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に`AppError::NotFound`、
/// パーティが1体も編成されていない場合に`AppError::BadRequest`を返す。
#[utoipa::path(
    post,
    path = "/battle/queue",
    responses(
        (status = 200, description = "待機列への参加成功(ボディ無し)"),
        (status = 400, description = "パーティが1体も編成されていない"),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "battle",
)]
pub async fn join_queue_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
) -> Result<(), AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    let party = player_pachimon_service::list_party(&state.pool, &player.player_id).await?;
    if party.is_empty() {
        return Err(AppError::BadRequest("party is empty".to_string()));
    }

    matchmaking_service::join(&state.matchmaking, &state.battle, &player.player_id)
}

/// マッチング待機列から離脱するAPIハンドラ。待機列にいない場合も成功扱い。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に`AppError::NotFound`を返す。
#[utoipa::path(
    delete,
    path = "/battle/queue",
    responses(
        (status = 200, description = "待機列からの離脱成功(ボディ無し)"),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "battle",
)]
pub async fn leave_queue_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
) -> Result<(), AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    matchmaking_service::leave(&state.matchmaking, &player.player_id)
}

/// マッチ成立を確認するAPIハンドラ(polling用)。
///
/// マッチ成立済みなら接続情報を返し、そのマッチ結果はサーバー側から削除される
/// (2回目以降の呼び出しでは返らない)。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に`AppError::NotFound`を返す。
#[utoipa::path(
    get,
    path = "/battle/queue/status",
    responses(
        (status = 200, description = "マッチング状況", body = QueueStatusResponse),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "battle",
)]
pub async fn queue_status_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
) -> Result<Json<QueueStatusResponse>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    let response = match matchmaking_service::take_match(&state.matchmaking, &player.player_id)? {
        Some(info) => QueueStatusResponse {
            status: STATUS_MATCHED.to_string(),
            match_id: info.match_id,
            battle_server: info.battle_server,
            battle_token: info.battle_token,
        },
        None => QueueStatusResponse {
            status: STATUS_WAITING.to_string(),
            match_id: String::new(),
            battle_server: String::new(),
            battle_token: String::new(),
        },
    };

    Ok(Json(response))
}
