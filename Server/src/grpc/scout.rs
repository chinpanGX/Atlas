use std::sync::Arc;

use chrono::Utc;
use tonic::{Request, Response, Status};
use voltech_grpc_error::{failed_precondition, internal};
use voltech_grpc_transaction::Transactions;

use crate::grpc::context::{player_id, to_ulid};
use crate::grpc::player_data::diff_to_proto;
use crate::master::{self, MasterData};
use crate::proto::atlas::v1::scout_service_server::{ScoutService, ScoutServiceServer};
use crate::proto::atlas::v1::{
    ListBannersRequest, ListBannersResponse, RollRequest, RollResponse, ScoutBanner,
    ScoutCandidate, SelectRequest, SelectResponse,
};
use crate::proto::errors;
use crate::service::game_log::GameLogCollector;
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::scout_service::{self, ScoutError};

/// `ScoutService`(スカウトのバナー一覧・ロール・候補の選択)の実装。
#[derive(Clone)]
pub struct ScoutServiceImpl {
    transactions: Transactions,
    master: Arc<MasterData>,
}

impl ScoutServiceImpl {
    pub fn new(transactions: Transactions, master: Arc<MasterData>) -> Self {
        Self {
            transactions,
            master,
        }
    }

    /// tonic のルーターに登録できる形にする。`AuthLayer` を付けて登録すること。
    pub fn into_server(self) -> ScoutServiceServer<Self> {
        ScoutServiceServer::new(self)
    }
}

#[tonic::async_trait]
impl ScoutService for ScoutServiceImpl {
    async fn list_banners(
        &self,
        request: Request<ListBannersRequest>,
    ) -> Result<Response<ListBannersResponse>, Status> {
        player_id(&request)?;
        let master = self.master.clone();

        let response = self
            .transactions
            .run(&request, None, move |_| {
                Box::pin(async move {
                    let banners =
                        scout_service::list_active_banners(&master, Utc::now().naive_utc());
                    Ok(ListBannersResponse {
                        banners: banners.into_iter().map(banner_to_proto).collect(),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn roll(&self, request: Request<RollRequest>) -> Result<Response<RollResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;
        let banner_id = request.get_ref().banner_id.clone();
        let master = self.master.clone();

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |ctx| {
                Box::pin(async move {
                    let mut diff = PlayerDiffCollector::default();
                    let mut logs = GameLogCollector::default();
                    let roll = scout_service::create_roll(
                        ctx.tx(),
                        &mut diff,
                        &mut logs,
                        &master,
                        &player_id,
                        &banner_id,
                    )
                    .await
                    .map_err(to_status)?;
                    logs.write_to(ctx)?;
                    let candidates = roll
                        .candidates
                        .into_iter()
                        .zip(0..)
                        .map(|(candidate, index)| ScoutCandidate {
                            index,
                            pachimon_id: candidate.pachimon_id,
                            move_ids: candidate.moves,
                        })
                        .collect();
                    Ok(RollResponse {
                        roll_id: roll.roll_id,
                        candidates,
                        player_diff: diff_to_proto(&diff),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn select(
        &self,
        request: Request<SelectRequest>,
    ) -> Result<Response<SelectResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;
        let SelectRequest { roll_id, index } = request.get_ref().clone();

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |ctx| {
                Box::pin(async move {
                    let mut diff = PlayerDiffCollector::default();
                    scout_service::select_candidate(
                        ctx.tx(),
                        &mut diff,
                        &player_id,
                        &roll_id,
                        index,
                    )
                    .await
                    .map_err(to_status)?;
                    Ok(SelectResponse {
                        player_diff: diff_to_proto(&diff),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }
}

/// 排出率は抽選の生の値なので、クライアントに渡さない。
fn banner_to_proto(banner: &master::ScoutBanner) -> ScoutBanner {
    ScoutBanner {
        banner_id: banner.banner_id.clone(),
        name: banner.name.clone(),
        cost_per_roll: banner.cost_per_roll,
        start_at: Some(to_timestamp(banner.start_at)),
        end_at: Some(to_timestamp(banner.end_at)),
    }
}

/// バナーの日時は UTC。
fn to_timestamp(at: chrono::NaiveDateTime) -> prost_types::Timestamp {
    let at = at.and_utc();
    prost_types::Timestamp {
        seconds: at.timestamp(),
        nanos: at.timestamp_subsec_nanos().cast_signed(),
    }
}

fn to_status(err: ScoutError) -> Status {
    match err {
        ScoutError::BannerNotFound => {
            failed_precondition(errors::SCOUT_BANNER_NOT_FOUND, "scout banner is not found")
        }
        ScoutError::BannerClosed => {
            failed_precondition(errors::SCOUT_BANNER_CLOSED, "scout banner is not active")
        }
        ScoutError::InsufficientGems => {
            failed_precondition(errors::INSUFFICIENT_GEMS, "not enough gems")
        }
        ScoutError::RollNotFound => {
            failed_precondition(errors::SCOUT_ROLL_NOT_FOUND, "scout roll is not found")
        }
        ScoutError::CandidateOutOfRange => failed_precondition(
            errors::SCOUT_CANDIDATE_OUT_OF_RANGE,
            "candidate index is out of range",
        ),
        ScoutError::RollAlreadySelected => failed_precondition(
            errors::SCOUT_ROLL_ALREADY_SELECTED,
            "scout roll is already selected",
        ),
        ScoutError::Internal(err) => internal(err),
    }
}
