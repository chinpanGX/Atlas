use axum::{Json, extract::State};
use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

use crate::error::AppError;
use crate::extractor::AuthenticatedDevice;
use crate::service::{player_item_service, player_pachimon_service, player_service};
use crate::state::AppState;

/// プレイヤー作成APIのリクエストボディ。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct CreatePlayerRequest {
    pub nickname: String,
}

/// 新規プレイヤーを作成し、認証済みデバイスに紐付けるAPIハンドラ。
///
/// 1つの`device_id`につきプレイヤーは1件のみで、既に作成済みの場合は`409`を返す。
/// 作成と同時にスターターパーティ・初期アイテム(item_id=1のジェム300個)が付与される。
/// レスポンスボディは持たない(以降のデータ取得は`POST /sign-in`で行う)。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、既にプレイヤーが存在する場合に
/// `AppError::Conflict`を返す。
#[utoipa::path(
    post,
    path = "/signup",
    request_body = CreatePlayerRequest,
    responses(
        (status = 200, description = "プレイヤー作成成功(ボディ無し)"),
        (status = 401, description = "未認証"),
        (status = 409, description = "このデバイスには既にプレイヤーが存在する"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
pub async fn signup_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Json(req): Json<CreatePlayerRequest>,
) -> Result<(), AppError> {
    player_service::create(&state.pool, &state.master, &device.device_id, &req.nickname).await?;

    Ok(())
}

/// 所持パチモン1体分のレスポンスDTO。覚えている技は`pachimonMoveMap`(`PlayerDiffDto`)側で
/// 別途持つため含まない。パーティ編成状況も同様に`partySlots`側で別途持つ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PlayerPachimonDto {
    pub player_pachimon_id: String,
    pub pachimon_id: i64,
}

/// 所持パチモン1体分の、覚えている技のレスポンスDTO。`playerPachimonMoveId`は割当自体のULID
/// (`Shared/docs/design/outgame.md`参照)。`playerPachimonId`は、`pachimon`側とのネストを
/// 解消して`PlayerDiffDto`上の独立したリソースとして扱うために持つ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PlayerPachimonMoveDto {
    pub player_pachimon_move_id: String,
    pub player_pachimon_id: String,
    pub slot: i32,
    pub move_id: i64,
}

/// パーティ編成1割当分のレスポンスDTO。`partySlotId`は割当自体のULID
/// (`Shared/docs/design/outgame.md`参照)。パーティに入っていないslotはこのリソースに
/// 含まれない(行自体が存在しないため、`nullable`を使わずに「未編成」を表現できる)。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PartySlotDto {
    pub party_slot_id: String,
    pub slot: i32,
    pub player_pachimon_id: String,
}

/// プレイヤー所持アイテム1件分のレスポンスDTO。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct PlayerItemDto {
    pub item_id: i64,
    pub quantity: i32,
}

/// `items`リソースの差分。所持数0になった場合でも、現状は行を削除しないため`removed`は
/// 使われない(将来アイテムを完全に手放す仕様が入った場合のために型としては持たせておく)。
#[derive(Serialize, ToSchema, Default)]
#[serde(rename_all = "camelCase")]
pub struct ItemsDiffDto {
    pub upserted: Vec<PlayerItemDto>,
    pub removed: Vec<i64>,
}

/// `pachimon`リソースの差分。
#[derive(Serialize, ToSchema, Default)]
#[serde(rename_all = "camelCase")]
pub struct PachimonDiffDto {
    pub upserted: Vec<PlayerPachimonDto>,
    pub removed: Vec<String>,
}

/// `pachimonMoveMap`(所持パチモンが覚えている技)リソースの差分。
#[derive(Serialize, ToSchema, Default)]
#[serde(rename_all = "camelCase")]
pub struct PachimonMoveMapDiffDto {
    pub upserted: Vec<PlayerPachimonMoveDto>,
    pub removed: Vec<String>,
}

/// `partySlots`リソースの差分。パーティ編成は全置き換えのため、編成し直すたびに
/// 以前の`partySlotId`は全て`removed`に、新しい`partySlotId`が`upserted`に入る。
#[derive(Serialize, ToSchema, Default)]
#[serde(rename_all = "camelCase")]
pub struct PartySlotsDiffDto {
    pub upserted: Vec<PartySlotDto>,
    pub removed: Vec<String>,
}

/// プレイヤーの永続データの差分。`playerDiff`を返す全APIで共通利用する
/// (`Shared/docs/design/outgame.md`参照)。各リソースは影響が無い場合、
/// `upserted`/`removed`が両方空の状態で含まれる。
#[derive(Serialize, ToSchema, Default)]
#[serde(rename_all = "camelCase")]
pub struct PlayerDiffDto {
    pub items: ItemsDiffDto,
    pub pachimon: PachimonDiffDto,
    pub pachimon_move_map: PachimonMoveMapDiffDto,
    pub party_slots: PartySlotsDiffDto,
}

/// 認証済みプレイヤーの所持データ全件を`PlayerDiffDto`として組み立てる
/// (`POST /sign-in`専用。`removed`は常に空)。
async fn build_full_player_diff(
    pool: &sqlx::MySqlPool,
    player_id: &str,
) -> Result<PlayerDiffDto, AppError> {
    let owned = player_pachimon_service::list_owned_pachimon(pool, player_id).await?;
    let moves = player_pachimon_service::list_owned_moves(pool, player_id).await?;
    let party = player_pachimon_service::list_party(pool, player_id).await?;
    let items = player_item_service::list(pool, player_id).await?;

    Ok(PlayerDiffDto {
        items: ItemsDiffDto {
            upserted: items
                .into_iter()
                .map(|i| PlayerItemDto {
                    item_id: i.item_id,
                    quantity: i.quantity,
                })
                .collect(),
            removed: Vec::new(),
        },
        pachimon: PachimonDiffDto {
            upserted: owned
                .into_iter()
                .map(|p| PlayerPachimonDto {
                    player_pachimon_id: p.player_pachimon_id,
                    pachimon_id: p.pachimon_id,
                })
                .collect(),
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
        party_slots: PartySlotsDiffDto {
            upserted: party
                .into_iter()
                .map(|s| PartySlotDto {
                    party_slot_id: s.party_slot_id,
                    slot: s.slot,
                    player_pachimon_id: s.player_pachimon_id,
                })
                .collect(),
            removed: Vec::new(),
        },
    })
}

/// サインインAPIのレスポンスボディ。
#[derive(Serialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct SignInResponse {
    pub player_id: String,
    pub nickname: String,
    pub player_diff: PlayerDiffDto,
}

/// 認証済みデバイスに紐づくプレイヤー情報・所持データ全件を取得するAPIハンドラ。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に
/// `AppError::NotFound`を返す。
#[utoipa::path(
    post,
    path = "/sign-in",
    responses(
        (status = 200, description = "プレイヤー情報・所持データ全件", body = SignInResponse),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
pub async fn sign_in_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
) -> Result<Json<SignInResponse>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;
    let player_diff = build_full_player_diff(&state.pool, &player.player_id).await?;

    Ok(Json(SignInResponse {
        player_id: player.player_id,
        nickname: player.nickname,
        player_diff,
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

/// バトル用パーティ(1-6体)を編成するAPIハンドラ。既存の編成は全置き換えされる
/// (`Shared/docs/design/outgame.md`「9. パーティ編成」のバリデーション参照)。
///
/// # Errors
/// 未認証の場合に`AppError::Unauthorized`、プレイヤー未作成の場合に`AppError::NotFound`
/// (指定した`playerPachimonId`が自分の所持個体でない場合も同様)、人数・slot・
/// `playerPachimonId`の重複などのバリデーション違反の場合に`AppError::BadRequest`を返す。
#[utoipa::path(
    post,
    path = "/edit/party",
    request_body = SetPartyRequest,
    responses(
        (status = 200, description = "パーティ編成成功(partySlotsのみ更新)", body = PlayerDiffDto),
        (status = 400, description = "人数・slot・playerPachimonIdの重複などのバリデーション違反"),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成、または指定したplayerPachimonIdが自分の所持個体でない"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
pub async fn edit_party_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Json(req): Json<SetPartyRequest>,
) -> Result<Json<PlayerDiffDto>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    let slots = req
        .party_slots
        .into_iter()
        .map(|s| player_pachimon_service::PartySlotInput {
            slot: s.slot,
            player_pachimon_id: s.player_pachimon_id,
        })
        .collect();

    let (result, removed) =
        player_pachimon_service::set_party(&state.pool, &player.player_id, slots).await?;

    Ok(Json(PlayerDiffDto {
        party_slots: PartySlotsDiffDto {
            upserted: result
                .into_iter()
                .map(|s| PartySlotDto {
                    party_slot_id: s.party_slot_id,
                    slot: s.slot,
                    player_pachimon_id: s.player_pachimon_id,
                })
                .collect(),
            removed,
        },
        ..Default::default()
    }))
}

/// 技の付け替えAPIのリクエストボディ。
#[derive(Deserialize, ToSchema)]
#[serde(rename_all = "camelCase")]
pub struct EditPachimonMoveRequest {
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
    post,
    path = "/edit/pachimon_moves",
    request_body = EditPachimonMoveRequest,
    responses(
        (status = 200, description = "付け替え成功(pachimonMoveMapのみ更新)", body = PlayerDiffDto),
        (status = 400, description = "slotが範囲外、またはmoveIdが候補技でない"),
        (status = 401, description = "未認証"),
        (status = 404, description = "プレイヤー未作成、または指定したplayerPachimonIdが自分の所持個体でない"),
    ),
    security(("bearer_auth" = [])),
    tag = "player",
)]
pub async fn edit_pachimon_move_handler(
    State(state): State<AppState>,
    device: AuthenticatedDevice,
    Json(req): Json<EditPachimonMoveRequest>,
) -> Result<Json<PlayerDiffDto>, AppError> {
    let player = player_service::find_by_device_id(&state.pool, &device.device_id).await?;

    let updated = player_pachimon_service::update_move(
        &state.pool,
        &state.master,
        &player.player_id,
        &req.player_pachimon_id,
        req.slot,
        req.move_id,
    )
    .await?;

    Ok(Json(PlayerDiffDto {
        pachimon_move_map: PachimonMoveMapDiffDto {
            upserted: vec![PlayerPachimonMoveDto {
                player_pachimon_move_id: updated.player_pachimon_move_id,
                player_pachimon_id: updated.player_pachimon_id,
                slot: updated.slot,
                move_id: updated.move_id,
            }],
            removed: Vec::new(),
        },
        ..Default::default()
    }))
}
