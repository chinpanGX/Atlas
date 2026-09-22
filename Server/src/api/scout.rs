use axum::{
    Json,
    extract::{Path, State},
};
use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

use crate::api::player::{
    ItemsDiffDto, PachimonDiffDto, PachimonMoveMapDiffDto, PlayerDiffDto, PlayerItemDto,
    PlayerPachimonDto, PlayerPachimonMoveDto,
};
use crate::error::AppError;
use crate::extractor::AuthenticatedDevice;
use crate::service::{player_item_service, player_service, scout_service};
use crate::state::AppState;

/// スカウトバナー1件分のレスポンスDTO。排出率(`rate_table`)は含めない
/// (`Shared/docs/design/scout.md`参照)。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct BannerDto {
    pub banner_id: String,
    pub name: String,
    pub cost_per_roll: i32,
    pub start_at: chrono::NaiveDateTime,
    pub end_at: chrono::NaiveDateTime,
}

/// スカウトバナー一覧APIのレスポンスボディ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct ListBannersResponse {
    pub banners: Vec<BannerDto>,
}

/// 開催中のスカウトバナー一覧を取得するAPIハンドラ。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`を返す。
#[utoipa::path(
    get,
    path = "/scout/banners",
    responses(
        (status = 200, description = "開催中のスカウトバナー一覧", body = ListBannersResponse),
        (status = 401, description = "未認証"),
    ),
    security(("bearer_auth" = [])),
    tag = "scout",
)]
pub async fn list_banners_handler(
    State(state): State<AppState>,
    _device: AuthenticatedDevice,
) -> Result<Json<ListBannersResponse>, AppError> {
    let banners = scout_service::list_active_banners(&state.pool).await?;

    Ok(Json(ListBannersResponse {
        banners: banners
            .into_iter()
            .map(|banner| BannerDto {
                banner_id: banner.banner_id,
                name: banner.name,
                cost_per_roll: banner.cost_per_roll,
                start_at: banner.start_at,
                end_at: banner.end_at,
            })
            .collect(),
    }))
}

/// 紹介を受けるAPIのリクエストボディ。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct CreateRollRequest {
    pub banner_id: String,
}

/// 紹介の候補1体分のレスポンスDTO。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct CandidateDto {
    pub index: usize,
    pub pachimon_id: i64,
    pub rarity: String,
    pub moves: Vec<i64>,
}

/// 紹介を受けるAPIのレスポンスボディ。ジェム消費結果は`playerDiff.items`側に含まれる
/// (`pachimon`/`pachimonMoveMap`/`partySlots`は未変更のため空)。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct CreateRollResponse {
    pub roll_id: String,
    pub candidates: Vec<CandidateDto>,
    pub player_diff: PlayerDiffDto,
}

/// 紹介を受け、ジェムを消費して候補10体をロールするAPIハンドラ。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、バナーが存在しない場合に`AppError::NotFound`、
/// バナーが開催期間外またはジェムが不足している場合に`AppError::BadRequest`を返す。
#[utoipa::path(
    post,
    path = "/scout/rolls",
    request_body = CreateRollRequest,
    responses(
        (status = 200, description = "紹介成功", body = CreateRollResponse),
        (status = 400, description = "バナー開催期間外またはジェム不足"),
        (status = 401, description = "未認証"),
        (status = 404, description = "バナー未存在"),
    ),
    security(("bearer_auth" = [])),
    tag = "scout",
)]
pub async fn create_roll_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Json(req): Json<CreateRollRequest>,
) -> Result<Json<CreateRollResponse>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;
    let (roll, remaining_gems) = scout_service::create_roll(
        &state.pool,
        &state.master,
        &player.player_id,
        &req.banner_id,
    )
    .await?;

    Ok(Json(CreateRollResponse {
        roll_id: roll.roll_id,
        candidates: roll
            .candidates
            .into_iter()
            .enumerate()
            .map(|(index, candidate)| CandidateDto {
                index,
                pachimon_id: candidate.pachimon_id,
                rarity: candidate.rarity,
                moves: candidate.moves,
            })
            .collect(),
        player_diff: PlayerDiffDto {
            items: ItemsDiffDto {
                upserted: vec![PlayerItemDto {
                    item_id: player_item_service::GEM_ITEM_ID,
                    quantity: remaining_gems.quantity,
                }],
                removed: Vec::new(),
            },
            ..Default::default()
        },
    }))
}

/// 候補から1体を選ぶAPIのリクエストボディ。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct SelectRollRequest {
    pub index: i32,
}

/// 保存済みの候補から1体を選んで恒久的に入手するAPIハンドラ。レスポンスは`playerDiff`
/// (`pachimon`/`pachimonMoveMap`のみ更新、`items`/`partySlots`は空)。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、rollが存在しない・自分のものでない場合に
/// `AppError::NotFound`、`index`が範囲外の場合に`AppError::BadRequest`、
/// 既に選択済みの場合に`AppError::Conflict`を返す。
#[utoipa::path(
    post,
    path = "/scout/rolls/{rollId}/select",
    params(("rollId" = String, Path, description = "紹介ID")),
    request_body = SelectRollRequest,
    responses(
        (status = 200, description = "入手成功(pachimon/pachimonMoveMapのみ更新)", body = PlayerDiffDto),
        (status = 400, description = "indexが範囲外"),
        (status = 401, description = "未認証"),
        (status = 404, description = "roll未存在"),
        (status = 409, description = "既に選択済み"),
    ),
    security(("bearer_auth" = [])),
    tag = "scout",
)]
pub async fn select_roll_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Path(roll_id): Path<String>,
    Json(req): Json<SelectRollRequest>,
) -> Result<Json<PlayerDiffDto>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;
    let (player_pachimon, moves) =
        scout_service::select_candidate(&state.pool, &player.player_id, &roll_id, req.index)
            .await?;

    Ok(Json(PlayerDiffDto {
        pachimon: PachimonDiffDto {
            upserted: vec![PlayerPachimonDto {
                player_pachimon_id: player_pachimon.player_pachimon_id,
                pachimon_id: player_pachimon.pachimon_id,
            }],
            removed: Vec::new(),
        },
        pachimon_move_map: PachimonMoveMapDiffDto {
            upserted: moves
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
        ..Default::default()
    }))
}
