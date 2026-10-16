use std::sync::{Arc, Mutex};

use tonic::{Request, Response, Status};
use voltech_grpc_error::{ResultExt, failed_precondition};
use voltech_grpc_transaction::Transactions;

use crate::grpc::context::{player_id, to_ulid};
use crate::proto::atlas::v1::matchmaking_service_server::{
    MatchmakingService, MatchmakingServiceServer,
};
use crate::proto::atlas::v1::{
    GetQueueStatusRequest, GetQueueStatusResponse, JoinQueueRequest, JoinQueueResponse,
    LeaveQueueRequest, LeaveQueueResponse, QueueStatus,
};
use crate::proto::errors;
use crate::service::matchmaking_service::{self, BattleConfig, MatchmakingQueue};
use crate::service::player_pachimon_service;

/// `MatchmakingService`(待機列への参加・離脱・状況の確認)の実装。待機列は内部APIと同じものを使う。
#[derive(Clone)]
pub struct MatchmakingServiceImpl {
    transactions: Transactions,
    queue: Arc<Mutex<MatchmakingQueue>>,
    config: Arc<BattleConfig>,
}

impl MatchmakingServiceImpl {
    pub fn new(
        transactions: Transactions,
        queue: Arc<Mutex<MatchmakingQueue>>,
        config: Arc<BattleConfig>,
    ) -> Self {
        Self {
            transactions,
            queue,
            config,
        }
    }

    /// tonic のルーターに登録できる形にする。`AuthLayer` を付けて登録すること。
    pub fn into_server(self) -> MatchmakingServiceServer<Self> {
        MatchmakingServiceServer::new(self)
    }
}

#[tonic::async_trait]
impl MatchmakingService for MatchmakingServiceImpl {
    async fn join_queue(
        &self,
        request: Request<JoinQueueRequest>,
    ) -> Result<Response<JoinQueueResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;
        let queue = self.queue.clone();
        let config = self.config.clone();

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |ctx| {
                Box::pin(async move {
                    let party = player_pachimon_service::list_party(&mut **ctx.tx(), &player_id)
                        .await
                        .or_internal()?;
                    if party.is_empty() {
                        return Err(failed_precondition(errors::PARTY_EMPTY, "party is empty"));
                    }
                    matchmaking_service::join(ctx.tx(), &queue, &config, &player_id)
                        .await
                        .or_internal()?;
                    Ok(JoinQueueResponse {})
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn leave_queue(
        &self,
        request: Request<LeaveQueueRequest>,
    ) -> Result<Response<LeaveQueueResponse>, Status> {
        let player_id = player_id(&request)?;
        let queue = self.queue.clone();

        let response = self
            .transactions
            .run(&request, None, move |_| {
                Box::pin(async move {
                    matchmaking_service::leave(&queue, &player_id).or_internal()?;
                    Ok(LeaveQueueResponse {})
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn get_queue_status(
        &self,
        request: Request<GetQueueStatusRequest>,
    ) -> Result<Response<GetQueueStatusResponse>, Status> {
        let player_id = player_id(&request)?;
        let queue = self.queue.clone();

        let response = self
            .transactions
            .run(&request, None, move |_| {
                Box::pin(async move {
                    let info = matchmaking_service::find_match(&queue, &player_id).or_internal()?;
                    Ok(match info {
                        Some(info) => GetQueueStatusResponse {
                            status: QueueStatus::Matched.into(),
                            match_id: info.match_id,
                            battle_server: info.battle_server,
                            battle_token: info.battle_token,
                        },
                        None => GetQueueStatusResponse {
                            status: QueueStatus::Waiting.into(),
                            ..Default::default()
                        },
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }
}
