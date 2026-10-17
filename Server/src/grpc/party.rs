use std::sync::Arc;

use tonic::{Request, Response, Status};
use voltech_grpc_error::{failed_precondition, internal};
use voltech_grpc_transaction::Transactions;

use crate::grpc::context::{player_id, to_ulid};
use crate::grpc::player_data::{diff_to_proto, stat_points_from_proto};
use crate::master::MasterData;
use crate::proto::atlas::v1::party_service_server::{PartyService, PartyServiceServer};
use crate::proto::atlas::v1::{
    SetPachimonMoveRequest, SetPachimonMoveResponse, SetPachimonStatPointsRequest,
    SetPachimonStatPointsResponse, SetPartyRequest, SetPartyResponse,
};
use crate::proto::errors;
use crate::service::game_log::GameLogCollector;
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::player_pachimon_service::{self, MoveSlotInput, PartyError, PartySlotInput};

/// `PartyService`(パーティの編成・育成)の実装。
#[derive(Clone)]
pub struct PartyServiceImpl {
    transactions: Transactions,
    master: Arc<MasterData>,
}

impl PartyServiceImpl {
    pub fn new(transactions: Transactions, master: Arc<MasterData>) -> Self {
        Self {
            transactions,
            master,
        }
    }

    /// tonic のルーターに登録できる形にする。`AuthLayer` を付けて登録すること。
    pub fn into_server(self) -> PartyServiceServer<Self> {
        PartyServiceServer::new(self)
    }
}

#[tonic::async_trait]
impl PartyService for PartyServiceImpl {
    async fn set_party(
        &self,
        request: Request<SetPartyRequest>,
    ) -> Result<Response<SetPartyResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;
        let slots: Vec<PartySlotInput> = request
            .get_ref()
            .party_slots
            .iter()
            .map(|slot| PartySlotInput {
                slot: slot.slot,
                player_pachimon_id: slot.player_pachimon_id.clone(),
            })
            .collect();

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |ctx| {
                Box::pin(async move {
                    let mut diff = PlayerDiffCollector::default();
                    let mut logs = GameLogCollector::default();
                    player_pachimon_service::set_party(
                        ctx.tx(),
                        &mut diff,
                        &mut logs,
                        &player_id,
                        slots,
                    )
                    .await
                    .map_err(to_status)?;
                    logs.write_to(ctx)?;
                    Ok(SetPartyResponse {
                        player_diff: diff_to_proto(&diff),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn set_pachimon_move(
        &self,
        request: Request<SetPachimonMoveRequest>,
    ) -> Result<Response<SetPachimonMoveResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;
        let SetPachimonMoveRequest {
            player_pachimon_id,
            slot,
            move_id,
        } = request.get_ref().clone();
        let master = self.master.clone();

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |ctx| {
                Box::pin(async move {
                    let mut diff = PlayerDiffCollector::default();
                    let mut logs = GameLogCollector::default();
                    player_pachimon_service::update_move(
                        ctx.tx(),
                        &mut diff,
                        &mut logs,
                        &master,
                        &player_id,
                        MoveSlotInput {
                            player_pachimon_id,
                            slot,
                            move_id,
                        },
                    )
                    .await
                    .map_err(to_status)?;
                    logs.write_to(ctx)?;
                    Ok(SetPachimonMoveResponse {
                        player_diff: diff_to_proto(&diff),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn set_pachimon_stat_points(
        &self,
        request: Request<SetPachimonStatPointsRequest>,
    ) -> Result<Response<SetPachimonStatPointsResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;
        let SetPachimonStatPointsRequest {
            player_pachimon_id,
            stat_points,
        } = request.get_ref().clone();
        let stat_points = stat_points
            .map(stat_points_from_proto)
            .ok_or_else(|| to_status(PartyError::InvalidStatPoints))?;

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |ctx| {
                Box::pin(async move {
                    let mut diff = PlayerDiffCollector::default();
                    let mut logs = GameLogCollector::default();
                    player_pachimon_service::set_stat_points(
                        ctx.tx(),
                        &mut diff,
                        &mut logs,
                        &player_id,
                        &player_pachimon_id,
                        stat_points,
                    )
                    .await
                    .map_err(to_status)?;
                    logs.write_to(ctx)?;
                    Ok(SetPachimonStatPointsResponse {
                        player_diff: diff_to_proto(&diff),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }
}

fn to_status(err: PartyError) -> Status {
    match err {
        PartyError::InvalidParty(reason) => failed_precondition(errors::INVALID_PARTY, reason),
        PartyError::PachimonNotOwned => {
            failed_precondition(errors::PACHIMON_NOT_OWNED, "pachimon is not owned")
        }
        PartyError::InvalidMoveSlot => {
            failed_precondition(errors::INVALID_MOVE_SLOT, "slot must be between 1 and 4")
        }
        PartyError::MoveNotLearnable => failed_precondition(
            errors::MOVE_NOT_LEARNABLE,
            "move is not a candidate move for this pachimon",
        ),
        PartyError::MoveUnchanged => {
            failed_precondition(errors::MOVE_UNCHANGED, "the slot already has this move")
        }
        PartyError::MoveAlreadyLearned => failed_precondition(
            errors::MOVE_ALREADY_LEARNED,
            "the pachimon already knows this move in another slot",
        ),
        PartyError::InvalidStatPoints => failed_precondition(
            errors::INVALID_STAT_POINTS,
            "stat points must be non-negative and total at most 64",
        ),
        PartyError::InsufficientTrainingTickets => failed_precondition(
            errors::INSUFFICIENT_TRAINING_TICKETS,
            "not enough training tickets",
        ),
        PartyError::Internal(err) => internal(err),
    }
}
