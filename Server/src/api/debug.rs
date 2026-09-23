use axum::{Json, extract::State};

use crate::error::AppError;
use crate::extractor::AuthenticatedDevice;
use crate::service::{player_item_service, player_service};
use crate::state::AppState;

use super::player::{ItemsDiffDto, PlayerDiffDto, PlayerItemDto};

/// デバッグ用に付与するジェムの量。レギュラースカウト(1回150ジェム)3回分。
/// ゲーム内の正式な報酬ではなく、スカウトの動作確認をジェム切れせずに繰り返せるようにするための値。
const DEBUG_GRANT_GEMS_AMOUNT: i32 = 150 * 3;

/// 認証済みプレイヤーにジェムを付与する開発用APIハンドラ。ログインボーナス等の正式な
/// ゲーム内報酬ではなく、動作確認用の一時的なエンドポイント(連打防止・上限は設けていない)。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に`AppError::NotFound`を返す。
#[utoipa::path(
    post,
    path = "/debug/grant_gems",
    responses(
        (status = 200, description = "付与成功(itemsのみ更新)", body = PlayerDiffDto),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "debug",
)]
pub async fn grant_gems_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
) -> Result<Json<PlayerDiffDto>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    let mut tx = state
        .pool
        .begin()
        .await
        .map_err(|_| AppError::InternalError)?;
    let item = player_item_service::add_and_get(
        &mut tx,
        &player.player_id,
        player_item_service::GEM_ITEM_ID,
        DEBUG_GRANT_GEMS_AMOUNT,
    )
    .await?;
    tx.commit().await.map_err(|_| AppError::InternalError)?;

    Ok(Json(PlayerDiffDto {
        items: ItemsDiffDto {
            upserted: vec![PlayerItemDto {
                item_id: item.item_id,
                quantity: item.quantity,
            }],
            removed: Vec::new(),
        },
        ..Default::default()
    }))
}
