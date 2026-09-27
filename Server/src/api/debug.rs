use axum::{Json, extract::State};

use crate::error::AppError;
use crate::extractor::AuthenticatedDevice;
use crate::service::{player_item_service, player_pachimon_service, player_service};
use crate::state::AppState;

use super::player::{
    ItemsDiffDto, PachimonDiffDto, PachimonMoveMapDiffDto, PartySlotDto, PartySlotsDiffDto,
    PlayerDiffDto, PlayerItemDto, PlayerPachimonDto, PlayerPachimonMoveDto,
};

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

/// 認証済みプレイヤーのパーティを、pachimonマスタからランダムに選んだ6体(重複なし)で組み直す
/// 開発用APIハンドラ。対戦相手ボット(`BattleServer/BattleBot`)が毎回違う編成で対戦するためのもので、
/// 6体は新たに付与した個体(技は技グループの候補技からランダムに最大4つ)。以前の所持個体は残る。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に`AppError::NotFound`を返す。
#[utoipa::path(
    post,
    path = "/debug/randomize_party",
    responses(
        (status = 200, description = "編成成功(pachimon/pachimonMoveMap/partySlotsを更新)", body = PlayerDiffDto),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "debug",
)]
pub async fn randomize_party_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
) -> Result<Json<PlayerDiffDto>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    let party =
        player_pachimon_service::randomize_party(&state.pool, &state.master, &player.player_id)
            .await?;

    Ok(Json(PlayerDiffDto {
        pachimon: PachimonDiffDto {
            upserted: party
                .pachimon
                .into_iter()
                .map(|p| PlayerPachimonDto {
                    player_pachimon_id: p.player_pachimon_id,
                    pachimon_id: p.pachimon_id,
                })
                .collect(),
            removed: Vec::new(),
        },
        pachimon_move_map: PachimonMoveMapDiffDto {
            upserted: party
                .moves
                .into_iter()
                .map(|m| PlayerPachimonMoveDto {
                    player_pachimon_move_id: m.player_pachimon_move_id,
                    player_pachimon_id: m.player_pachimon_id,
                    slot: m.slot,
                    move_id: m.move_id,
                })
                .collect(),
            removed: Vec::new(),
        },
        party_slots: PartySlotsDiffDto {
            upserted: party
                .party_slots
                .into_iter()
                .map(|s| PartySlotDto {
                    party_slot_id: s.party_slot_id,
                    slot: s.slot,
                    player_pachimon_id: s.player_pachimon_id,
                })
                .collect(),
            removed: party.removed_party_slot_ids,
        },
        ..Default::default()
    }))
}
