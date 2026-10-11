use std::sync::Arc;

use tonic::{Request, Response, Status};
use voltech_grpc_transaction::Transactions;

use crate::grpc::context::{internal_error, player_id, to_ulid};
use crate::grpc::player_data::diff_to_proto;
use crate::master::MasterData;
use crate::proto::atlas::v1::debug_service_server::{DebugService, DebugServiceServer};
use crate::proto::atlas::v1::{
    GrantGemsRequest, GrantGemsResponse, RandomizePartyRequest, RandomizePartyResponse,
};
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::{player_item_service, player_pachimon_service};

/// 1回の付与で増やすジェムの数(レギュラースカウト3回分)。
const GRANT_GEMS_AMOUNT: i32 = 150 * 3;

/// `DebugService`(開発用のジェム付与・ランダム編成)の実装。
#[derive(Clone)]
pub struct DebugServiceImpl {
    transactions: Transactions,
    master: Arc<MasterData>,
}

impl DebugServiceImpl {
    pub fn new(transactions: Transactions, master: Arc<MasterData>) -> Self {
        Self {
            transactions,
            master,
        }
    }

    /// tonic のルーターに登録できる形にする。`AuthLayer` を付けて登録すること。
    pub fn into_server(self) -> DebugServiceServer<Self> {
        DebugServiceServer::new(self)
    }
}

#[tonic::async_trait]
impl DebugService for DebugServiceImpl {
    async fn grant_gems(
        &self,
        request: Request<GrantGemsRequest>,
    ) -> Result<Response<GrantGemsResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |tx| {
                Box::pin(async move {
                    let mut diff = PlayerDiffCollector::default();
                    let item = player_item_service::add_and_get(
                        tx,
                        &player_id,
                        player_item_service::GEM_ITEM_ID,
                        GRANT_GEMS_AMOUNT,
                    )
                    .await
                    .map_err(internal_error)?;
                    diff.record_item(item);
                    Ok(GrantGemsResponse {
                        player_diff: diff_to_proto(&diff),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn randomize_party(
        &self,
        request: Request<RandomizePartyRequest>,
    ) -> Result<Response<RandomizePartyResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;
        let master = self.master.clone();

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |tx| {
                Box::pin(async move {
                    let mut diff = PlayerDiffCollector::default();
                    player_pachimon_service::randomize_party(tx, &mut diff, &master, &player_id)
                        .await
                        .map_err(internal_error)?;
                    Ok(RandomizePartyResponse {
                        player_diff: diff_to_proto(&diff),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }
}
