//! BattleServer(C#/MagicOnion)から呼ばれる内部API。
//!
//! Unity Clientからは呼ばないため、OpenAPI(`ApiDoc`)には載せない
//! (載せると`api-codegen`がUnity向けの通信クライアントまで生成してしまう)。
use axum::{Json, extract::State};
use serde::{Deserialize, Serialize};
use serde_json::Value;

use crate::error::AppError;
use crate::extractor::InternalService;
use crate::service::battle_service::{self, BattleResultInput, BattleTurnInput};
use crate::service::player_pachimon_service::{self, EffortValues};
use crate::state::AppState;

/// 対戦結果報告APIのリクエストボディ(BattleServer側`BattleResultRequest`と同じ形)。
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BattleResultRequest {
    pub match_id: String,
    pub winner_id: String,
    pub player1_id: String,
    pub player2_id: String,
    pub player1_selected_pachimon: Vec<String>,
    pub player2_selected_pachimon: Vec<String>,
    pub turns: Vec<BattleTurnRequest>,
}

/// 対戦ログ1行分(`battle_turns`の1行)。`actionData`/`resultData`は中身を解釈せずJSONのまま保存する。
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BattleTurnRequest {
    pub turn_number: i32,
    pub player_id: String,
    pub action_data: Value,
    pub result_data: Value,
}

/// BattleServerからの対戦結果を記録するAPIハンドラ(`POST /internal/battle/result`)。
///
/// `battle_matches`の終了状態への更新・`battle_turns`の記録・勝者へのgems付与を行う。
///
/// # Errors
/// `X-Internal-Secret`が不一致の場合に`AppError::Unauthorized`、対戦が存在しない場合に
/// `AppError::NotFound`、参加者でないIDが含まれる場合に`AppError::BadRequest`、
/// 既に記録済み(二重報告)の場合に`AppError::Conflict`を返す。
pub async fn report_battle_result_handler(
    State(state): State<AppState>,
    _internal: InternalService,
    Json(req): Json<BattleResultRequest>,
) -> Result<(), AppError> {
    let input = BattleResultInput {
        match_id: req.match_id,
        winner_id: req.winner_id,
        player1_id: req.player1_id,
        player2_id: req.player2_id,
        player1_selected_pachimon: req.player1_selected_pachimon,
        player2_selected_pachimon: req.player2_selected_pachimon,
        turns: req
            .turns
            .into_iter()
            .map(|turn| BattleTurnInput {
                turn_number: turn.turn_number,
                player_id: turn.player_id,
                action_data: turn.action_data,
                result_data: turn.result_data,
            })
            .collect(),
    };

    battle_service::record_result(&state.pool, &input).await
}

/// 対戦用の所持データ取得APIのリクエストボディ(BattleServer側`LoadoutRequest`と同じ形)。
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BattleLoadoutsRequest {
    pub player_id: String,
    pub player_pachimon_ids: Vec<String>,
}

/// 対戦用の所持データ取得APIのレスポンス。`pachimon`はリクエストの`playerPachimonIds`と同じ順番。
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BattleLoadoutsResponse {
    pub pachimon: Vec<BattleLoadoutResponse>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BattleLoadoutResponse {
    pub player_pachimon_id: String,
    pub pachimon_id: i32,
    pub effort_values: EffortValues,
    /// 覚えている技のmove_id(slot順)。
    pub move_ids: Vec<i32>,
}

/// BattleServerが選出を受け取ったときに、選出個体の対戦用データを返すAPIハンドラ
/// (`POST /internal/battle/loadouts`)。種族値・技の性能はBattleServerが自分のマスタから引くため、
/// ここではプレイヤーごとに異なる所持データ(どのパチモンか・努力値・覚えている技)だけを返す。
///
/// # Errors
/// `X-Internal-Secret`が不一致の場合に`AppError::Unauthorized`、`playerId`の所持でない個体が
/// 含まれる場合に`AppError::NotFound`を返す。
pub async fn battle_loadouts_handler(
    State(state): State<AppState>,
    _internal: InternalService,
    Json(req): Json<BattleLoadoutsRequest>,
) -> Result<Json<BattleLoadoutsResponse>, AppError> {
    let loadouts = player_pachimon_service::find_battle_loadouts(
        &state.pool,
        &req.player_id,
        &req.player_pachimon_ids,
    )
    .await?;

    Ok(Json(BattleLoadoutsResponse {
        pachimon: loadouts
            .into_iter()
            .map(|loadout| BattleLoadoutResponse {
                player_pachimon_id: loadout.player_pachimon_id,
                pachimon_id: loadout.pachimon_id,
                effort_values: loadout.effort_values,
                move_ids: loadout.moves.iter().map(|m| m.move_id).collect(),
            })
            .collect(),
    }))
}
