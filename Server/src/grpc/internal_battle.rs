use std::sync::{Arc, Mutex};

use serde_json::Value;
use sqlx::MySqlPool;
use subtle::ConstantTimeEq;
use tonic::{Request, Response, Status};

use crate::error::AppError;
use crate::proto::atlas::internal::v1::battle_internal_service_server::{
    BattleInternalService, BattleInternalServiceServer,
};
use crate::proto::atlas::internal::v1::{
    EffortValues, GetLoadoutsRequest, GetLoadoutsResponse, GetPartyRequest, GetPartyResponse,
    Loadout, PartyPachimon, ReportResultRequest, ReportResultResponse,
};
use crate::service::battle_service::{self, BattleResultInput, BattleTurnInput};
use crate::service::matchmaking_service::{self, MatchmakingQueue};
use crate::service::player_pachimon_service;

/// 共有シークレットを入れるメタデータのキー(BattleServer の `BattleInternalApi.SecretHeaderName` と同じ値)。
pub const INTERNAL_SECRET_HEADER: &str = "x-internal-secret";

/// `BattleInternalService`(バトルサーバー向けの内部API)の実装。
#[derive(Clone)]
pub struct BattleInternalServiceImpl {
    pool: MySqlPool,
    queue: Arc<Mutex<MatchmakingQueue>>,
}

impl BattleInternalServiceImpl {
    pub fn new(pool: MySqlPool, queue: Arc<Mutex<MatchmakingQueue>>) -> Self {
        Self { pool, queue }
    }

    /// `x-internal-secret` を `secret` と定数時間で比べる interceptor を付けて、tonic のルーターに登録できる形にする。
    pub fn into_server(
        self,
        secret: String,
    ) -> tonic::service::interceptor::InterceptedService<
        BattleInternalServiceServer<Self>,
        impl tonic::service::Interceptor + Clone,
    > {
        BattleInternalServiceServer::with_interceptor(self, move |request: Request<()>| {
            check_secret(&request, &secret)?;
            Ok(request)
        })
    }
}

fn check_secret(request: &Request<()>, secret: &str) -> Result<(), Status> {
    let given = request
        .metadata()
        .get(INTERNAL_SECRET_HEADER)
        .map(|value| value.as_bytes())
        .unwrap_or_default();
    if given.ct_eq(secret.as_bytes()).into() {
        Ok(())
    } else {
        Err(Status::unauthenticated("invalid internal secret"))
    }
}

#[tonic::async_trait]
impl BattleInternalService for BattleInternalServiceImpl {
    async fn get_party(
        &self,
        request: Request<GetPartyRequest>,
    ) -> Result<Response<GetPartyResponse>, Status> {
        let party =
            player_pachimon_service::list_battle_party(&self.pool, &request.get_ref().player_id)
                .await
                .map_err(to_status)?;

        Ok(Response::new(GetPartyResponse {
            pachimon: party
                .into_iter()
                .map(|member| PartyPachimon {
                    player_pachimon_id: member.player_pachimon_id,
                    pachimon_id: member.pachimon_id,
                })
                .collect(),
        }))
    }

    async fn get_loadouts(
        &self,
        request: Request<GetLoadoutsRequest>,
    ) -> Result<Response<GetLoadoutsResponse>, Status> {
        let request = request.get_ref();
        let loadouts = player_pachimon_service::find_battle_loadouts(
            &self.pool,
            &request.player_id,
            &request.player_pachimon_ids,
        )
        .await
        .map_err(to_status)?;

        Ok(Response::new(GetLoadoutsResponse {
            loadouts: loadouts
                .into_iter()
                .map(|loadout| Loadout {
                    player_pachimon_id: loadout.player_pachimon_id,
                    pachimon_id: loadout.pachimon_id,
                    effort_values: Some(EffortValues {
                        hp: loadout.effort_values.hp,
                        atk: loadout.effort_values.atk,
                        def: loadout.effort_values.def,
                        spatk: loadout.effort_values.spatk,
                        spdef: loadout.effort_values.spdef,
                        speed: loadout.effort_values.speed,
                    }),
                    move_ids: loadout.moves.iter().map(|m| m.move_id).collect(),
                })
                .collect(),
        }))
    }

    async fn report_result(
        &self,
        request: Request<ReportResultRequest>,
    ) -> Result<Response<ReportResultResponse>, Status> {
        let request = request.into_inner();
        let turns = request
            .turns
            .into_iter()
            .map(|turn| {
                Ok(BattleTurnInput {
                    turn_number: turn.turn_number,
                    player_id: turn.player_id,
                    action_data: parse_json(&turn.action_data_json)?,
                    result_data: parse_json(&turn.result_data_json)?,
                })
            })
            .collect::<Result<Vec<_>, Status>>()?;
        let input = BattleResultInput {
            match_id: request.match_id,
            winner_id: request.winner_id,
            player1_id: request.player1_id,
            player2_id: request.player2_id,
            player1_selected_pachimon: request.player1_selected_pachimon,
            player2_selected_pachimon: request.player2_selected_pachimon,
            turns,
        };

        battle_service::record_result(&self.pool, &input)
            .await
            .map_err(to_status)?;
        // 対戦が終わったので、マッチングの状況の確認で返していた接続情報はもう要らない
        matchmaking_service::clear_match(&self.queue, &input.match_id).map_err(to_status)?;
        Ok(Response::new(ReportResultResponse {}))
    }
}

/// 行動・結果は中身を解釈せずに JSON の列へ保存するが、JSON として読めないものは受け付けない。
fn parse_json(json: &str) -> Result<Value, Status> {
    serde_json::from_str(json)
        .map_err(|err| Status::invalid_argument(format!("invalid json: {err}")))
}

fn to_status(err: AppError) -> Status {
    match err {
        AppError::BadRequest(message) => Status::invalid_argument(message),
        AppError::NotFound => Status::not_found("not found"),
        AppError::Conflict => Status::already_exists("already recorded"),
        AppError::InternalError => Status::internal("internal error"),
    }
}
