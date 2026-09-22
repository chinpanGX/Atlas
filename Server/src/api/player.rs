use axum::{
    Json,
    extract::{Path, State},
};
use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

use crate::error::AppError;
use crate::extractor::AuthenticatedDevice;
use crate::service::{player_pachimon_service, player_service};
use crate::state::AppState;

/// プレイヤー作成APIのリクエストボディ。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct CreatePlayerRequest {
    pub nickname: String,
}

/// プレイヤー情報のレスポンスボディ。
#[derive(Serialize, ToSchema)]
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
#[utoipa::path(
    post,
    path = "/players",
    request_body = CreatePlayerRequest,
    responses(
        (status = 200, description = "プレイヤー作成成功", body = PlayerResponse),
        (status = 401, description = "未認証"),
        (status = 409, description = "このデバイスには既にプレイヤーが存在する"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
pub async fn create_player_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Json(req): Json<CreatePlayerRequest>,
) -> Result<Json<PlayerResponse>, AppError> {
    let player =
        player_service::create(&state.pool, &state.master, &device.device_id, &req.nickname)
            .await?;

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
#[utoipa::path(
    get,
    path = "/players/me",
    responses(
        (status = 200, description = "プレイヤー情報", body = PlayerResponse),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
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

/// 所持パチモン1体分の、覚えている技のレスポンスDTO。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PlayerPachimonMoveDto {
    pub slot: i32,
    pub move_id: i64,
}

/// 所持パチモン1体分のレスポンスDTO。パーティ編成状況(`partySlots`)は`player_party_slots`
/// (`PartySlotDto`)側で別途持つため含まない。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PlayerPachimonDto {
    pub player_pachimon_id: String,
    pub pachimon_id: i64,
    pub moves: Vec<PlayerPachimonMoveDto>,
}

/// パーティ編成1割当分のレスポンスDTO。`partySlotId`は割当自体のULID
/// (`Shared/docs/design/outgame.md`参照)。パーティに入っていないslotはこの配列に
/// 含まれない(行自体が存在しないため、`nullable`を使わずに「未編成」を表現できる)。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PartySlotDto {
    pub party_slot_id: String,
    pub slot: i32,
    pub player_pachimon_id: String,
}

/// 所持パチモン一覧APIのレスポンスボディ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct ListOwnedPachimonResponse {
    pub pachimon: Vec<PlayerPachimonDto>,
    pub party_slots: Vec<PartySlotDto>,
}

/// 認証済みプレイヤーの所持パチモン一覧・パーティ編成状況を取得するAPIハンドラ。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に
/// `AppError::NotFound`を返す。
#[utoipa::path(
    get,
    path = "/players/me/pachimon",
    responses(
        (status = 200, description = "所持パチモン一覧・パーティ編成状況", body = ListOwnedPachimonResponse),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
pub async fn list_owned_pachimon_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
) -> Result<Json<ListOwnedPachimonResponse>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;
    let owned = player_pachimon_service::list_owned(&state.pool, &player.player_id).await?;
    let party = player_pachimon_service::list_party(&state.pool, &player.player_id).await?;

    Ok(Json(ListOwnedPachimonResponse {
        pachimon: owned
            .into_iter()
            .map(|p| PlayerPachimonDto {
                player_pachimon_id: p.player_pachimon_id,
                pachimon_id: p.pachimon_id,
                moves: p
                    .moves
                    .into_iter()
                    .map(|m| PlayerPachimonMoveDto {
                        slot: m.slot,
                        move_id: m.move_id,
                    })
                    .collect(),
            })
            .collect(),
        party_slots: party
            .into_iter()
            .map(|p| PartySlotDto {
                party_slot_id: p.party_slot_id,
                slot: p.slot,
                player_pachimon_id: p.player_pachimon_id,
            })
            .collect(),
    }))
}

/// パーティ編成APIのリクエストボディ1slot分。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PartySlotRequest {
    pub slot: i32,
    pub player_pachimon_id: String,
}

/// パーティ編成APIのリクエストボディ。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct SetPartyRequest {
    pub party_slots: Vec<PartySlotRequest>,
}

/// パーティ編成APIのレスポンスボディ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct SetPartyResponse {
    pub party_slots: Vec<PartySlotDto>,
}

/// バトル用パーティ(1-6体)を編成するAPIハンドラ。`PUT`のため、既存の編成は全置き換えされる
/// (`Shared/docs/design/outgame.md`「9. パーティ編成」のバリデーション参照)。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に`AppError::NotFound`
/// (指定した`playerPachimonId`が自分の所持個体でない場合も同様)、人数・slot・
/// `playerPachimonId`の重複などのバリデーション違反の場合に`AppError::BadRequest`を返す。
#[utoipa::path(
    put,
    path = "/players/me/party",
    request_body = SetPartyRequest,
    responses(
        (status = 200, description = "パーティ編成成功", body = SetPartyResponse),
        (status = 400, description = "人数・slot・playerPachimonIdの重複などのバリデーション違反"),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成、または指定したplayerPachimonIdが自分の所持個体でない"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
pub async fn set_party_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Json(req): Json<SetPartyRequest>,
) -> Result<Json<SetPartyResponse>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    let slots = req
        .party_slots
        .into_iter()
        .map(|s| player_pachimon_service::PartySlotInput {
            slot: s.slot,
            player_pachimon_id: s.player_pachimon_id,
        })
        .collect();

    let result = player_pachimon_service::set_party(&state.pool, &player.player_id, slots).await?;

    Ok(Json(SetPartyResponse {
        party_slots: result
            .into_iter()
            .map(|s| PartySlotDto {
                party_slot_id: s.party_slot_id,
                slot: s.slot,
                player_pachimon_id: s.player_pachimon_id,
            })
            .collect(),
    }))
}

/// 技の付け替えAPIのリクエストボディ。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct UpdateMoveRequest {
    pub move_id: i64,
}

/// 技の付け替えAPIのレスポンスボディ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct UpdateMoveResponse {
    pub player_pachimon_id: String,
    pub slot: i32,
    pub move_id: i64,
}

/// 所持パチモンの技を付け替えるAPIハンドラ。グループ内の候補技(`move_group_moves`)から
/// 選択する(`Shared/docs/design/outgame.md`「10. 技の付け替え」参照)。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に`AppError::NotFound`
/// (指定した`playerPachimonId`が自分の所持個体でない場合も同様)、`slot`が範囲外または
/// `moveId`が候補技でない場合に`AppError::BadRequest`を返す。
#[utoipa::path(
    put,
    path = "/players/me/pachimon/{playerPachimonId}/moves/{slot}",
    params(
        ("playerPachimonId" = String, Path, description = "所持パチモンID"),
        ("slot" = i32, Path, description = "技スロット(1-4)"),
    ),
    request_body = UpdateMoveRequest,
    responses(
        (status = 200, description = "付け替え成功", body = UpdateMoveResponse),
        (status = 400, description = "slotが範囲外、またはmoveIdが候補技でない"),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成、または指定したplayerPachimonIdが自分の所持個体でない"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
pub async fn update_pachimon_move_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Path((player_pachimon_id, slot)): Path<(String, i32)>,
    Json(req): Json<UpdateMoveRequest>,
) -> Result<Json<UpdateMoveResponse>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    let updated = player_pachimon_service::update_move(
        &state.pool,
        &state.master,
        &player.player_id,
        &player_pachimon_id,
        slot,
        req.move_id,
    )
    .await?;

    Ok(Json(UpdateMoveResponse {
        player_pachimon_id,
        slot: updated.slot,
        move_id: updated.move_id,
    }))
}
