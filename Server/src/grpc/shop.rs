use std::sync::Arc;

use tonic::{Request, Response, Status};
use voltech_grpc_error::{failed_precondition, internal};
use voltech_grpc_transaction::Transactions;

use crate::grpc::context::{player_id, to_ulid};
use crate::grpc::player_data::diff_to_proto;
use crate::master::MasterData;
use crate::proto::atlas::v1::shop_service_server::{ShopService, ShopServiceServer};
use crate::proto::atlas::v1::{
    ListProductsRequest, ListProductsResponse, PurchaseRequest, PurchaseResponse, ShopProduct,
};
use crate::proto::errors;
use crate::service::game_log::GameLogCollector;
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::shop_service::{self, ShopError};

/// `ShopService`(ショップの商品一覧・購入)の実装。
#[derive(Clone)]
pub struct ShopServiceImpl {
    transactions: Transactions,
    master: Arc<MasterData>,
}

impl ShopServiceImpl {
    pub fn new(transactions: Transactions, master: Arc<MasterData>) -> Self {
        Self {
            transactions,
            master,
        }
    }

    /// tonic のルーターに登録できる形にする。`AuthLayer` を付けて登録すること。
    pub fn into_server(self) -> ShopServiceServer<Self> {
        ShopServiceServer::new(self)
    }
}

#[tonic::async_trait]
impl ShopService for ShopServiceImpl {
    async fn list_products(
        &self,
        request: Request<ListProductsRequest>,
    ) -> Result<Response<ListProductsResponse>, Status> {
        player_id(&request)?;
        let master = self.master.clone();

        let response = self
            .transactions
            .run(&request, None, move |_| {
                Box::pin(async move {
                    Ok(ListProductsResponse {
                        products: shop_service::list_products(&master)
                            .iter()
                            .map(|product| ShopProduct {
                                shop_product_id: product.shop_product_id,
                                item_id: product.item_id,
                                amount: product.amount,
                                cost_item_id: product.cost_item_id,
                                cost_amount: product.cost_amount,
                            })
                            .collect(),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }

    async fn purchase(
        &self,
        request: Request<PurchaseRequest>,
    ) -> Result<Response<PurchaseResponse>, Status> {
        let player_id = player_id(&request)?;
        let player_ulid = to_ulid(&player_id)?;
        let shop_product_id = request.get_ref().shop_product_id;
        let master = self.master.clone();

        let response = self
            .transactions
            .run(&request, Some(player_ulid), move |ctx| {
                Box::pin(async move {
                    let mut diff = PlayerDiffCollector::default();
                    let mut logs = GameLogCollector::default();
                    shop_service::purchase(
                        ctx.tx(),
                        &mut diff,
                        &mut logs,
                        &master,
                        &player_id,
                        shop_product_id,
                    )
                    .await
                    .map_err(to_status)?;
                    logs.write_to(ctx)?;
                    Ok(PurchaseResponse {
                        player_diff: diff_to_proto(&diff),
                    })
                })
            })
            .await?;
        Ok(Response::new(response))
    }
}

fn to_status(err: ShopError) -> Status {
    match err {
        ShopError::ProductNotFound => {
            failed_precondition(errors::SHOP_PRODUCT_NOT_FOUND, "shop product is not found")
        }
        ShopError::InsufficientGems => {
            failed_precondition(errors::INSUFFICIENT_GEMS, "not enough gems")
        }
        ShopError::Internal(err) => internal(err),
    }
}
