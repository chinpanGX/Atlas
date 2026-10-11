use std::sync::Arc;

use tonic::{Request, Response, Status};
use voltech_grpc_error::internal;
use voltech_grpc_transaction::Transactions;

use crate::grpc::context::{auth_context, internal_error, player_id, player_not_found};
use crate::grpc::player_data::{
    item_to_proto, move_to_proto, pachimon_to_proto, party_slot_to_proto,
};
use crate::master::MasterData;
use crate::proto::atlas::v1::player_service_server::{PlayerService, PlayerServiceServer};
use crate::proto::atlas::v1::{
    PlayerSnapshot, SignInRequest, SignInResponse, SignUpRequest, SignUpResponse,
};
use crate::service::{player_item_service, player_pachimon_service, player_service};

/// ニックネームの最大文字数(`players.nickname` の `VARCHAR(50)`)。
const MAX_NICKNAME_CHARS: usize = 50;

/// `PlayerService`(サインアップ・サインイン)の実装。
#[derive(Clone)]
pub struct PlayerServiceImpl {
    transactions: Transactions,
    master: Arc<MasterData>,
}

impl PlayerServiceImpl {
    pub fn new(transactions: Transactions, master: Arc<MasterData>) -> Self {
        Self {
            transactions,
            master,
        }
    }

    /// tonic のルーターに登録できる形にする。`AuthLayer` を付けて登録すること。
    pub fn into_server(self) -> PlayerServiceServer<Self> {
        PlayerServiceServer::new(self)
    }
}

#[tonic::async_trait]
impl PlayerService for PlayerServiceImpl {
    async fn sign_up(
        &self,
        request: Request<SignUpRequest>,
    ) -> Result<Response<SignUpResponse>, Status> {
        // サインアップの前なので、作成済みかは問わずに、デバイス登録で発行した player_id を使う
        let player_id = auth_context(&request)?.player_id.to_string();
        let nickname = request.get_ref().nickname.clone();
        let nickname_chars = nickname.chars().count();
        if nickname_chars == 0 || nickname_chars > MAX_NICKNAME_CHARS {
            return Err(Status::invalid_argument(
                "nickname must be 1 to 50 characters",
            ));
        }
        let master = self.master.clone();

        let response = self
            .transactions
            .run(&request, None, move |tx| {
                Box::pin(async move {
                    let player = player_service::create(tx, &master, &player_id, &nickname)
                        .await
                        .map_err(internal_error)?;
                    let ulid = ulid::Ulid::from_string(&player.player_id).map_err(internal)?;
                    // 冪等性の対象の RPC でロックする行を、プレイヤーと一緒に作っておく
                    voltech_grpc_transaction::create_last_response(tx, ulid)
                        .await
                        .map_err(internal)?;
                    Ok(SignUpResponse {
                        player_id: player.player_id,
                        nickname: player.nickname,
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn sign_in(
        &self,
        request: Request<SignInRequest>,
    ) -> Result<Response<SignInResponse>, Status> {
        let player_id = player_id(&request)?;

        let response = self
            .transactions
            .run(&request, None, move |tx| {
                Box::pin(async move {
                    let player = player_service::find_by_id(&mut **tx, &player_id)
                        .await
                        .map_err(internal_error)?
                        .ok_or_else(player_not_found)?;
                    let items = player_item_service::list(&mut **tx, &player_id)
                        .await
                        .map_err(internal_error)?;
                    let pachimon =
                        player_pachimon_service::list_owned_pachimon(&mut **tx, &player_id)
                            .await
                            .map_err(internal_error)?;
                    let moves = player_pachimon_service::list_owned_moves(&mut **tx, &player_id)
                        .await
                        .map_err(internal_error)?;
                    let party = player_pachimon_service::list_party(&mut **tx, &player_id)
                        .await
                        .map_err(internal_error)?;
                    Ok(SignInResponse {
                        player_id: player.player_id,
                        nickname: player.nickname,
                        snapshot: Some(PlayerSnapshot {
                            items: items.into_iter().map(item_to_proto).collect(),
                            pachimon: pachimon.into_iter().map(pachimon_to_proto).collect(),
                            pachimon_moves: moves.into_iter().map(move_to_proto).collect(),
                            party_slots: party.into_iter().map(party_slot_to_proto).collect(),
                        }),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }
}
