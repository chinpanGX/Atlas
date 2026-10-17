use sqlx::{MySql, Transaction};
use voltech_grpc_error::Internal;

use crate::error::AppError;
use crate::master::{MasterData, ShopProducts};
use crate::service::game_log::{GameLogCollector, ItemReason};
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::player_item_service;

/// ショップの購入の失敗。
#[derive(Debug)]
pub enum ShopError {
    /// 商品が無い。
    ProductNotFound,
    /// 値段のジェムが足りない。
    InsufficientGems,
    /// DBアクセスの失敗。
    Internal(Internal),
}

impl From<AppError> for ShopError {
    #[track_caller]
    fn from(err: AppError) -> Self {
        Self::Internal(Internal::from(err))
    }
}

/// 購入できる商品を`shop_product_id`の順に返す。販売期間は無いので、マスタにある商品をすべて返す。
pub fn list_products(master: &MasterData) -> &[ShopProducts] {
    &master.shop_products
}

/// 商品を1つ購入する。値段(`cost_item_id`を`cost_amount`個)を払い、`item_id`を`amount`個受け取る。
/// 払った後と受け取った後の所持アイテムを`diff`に、両方の増減をゲームログに記録する。呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// 商品が無い場合に`ShopError::ProductNotFound`、値段のジェムが足りない場合に`ShopError::InsufficientGems`、
/// DBアクセスに失敗した場合に`ShopError::Internal`を返す。
pub async fn purchase(
    tx: &mut Transaction<'_, MySql>,
    diff: &mut PlayerDiffCollector,
    logs: &mut GameLogCollector,
    master: &MasterData,
    player_id: &str,
    shop_product_id: i32,
) -> Result<(), ShopError> {
    let product = master
        .shop_products
        .iter()
        .find(|product| product.shop_product_id == shop_product_id)
        .ok_or(ShopError::ProductNotFound)?;

    // 払うアイテムがジェムであることは、seed_master_data がマスタを投入するときに確かめている
    let paid = player_item_service::deduct(
        tx,
        logs,
        player_id,
        product.cost_item_id,
        product.cost_amount,
        ItemReason::ShopPurchase,
    )
    .await?
    .ok_or(ShopError::InsufficientGems)?;
    let received = player_item_service::add(
        tx,
        logs,
        player_id,
        product.item_id,
        product.amount,
        ItemReason::ShopPurchase,
    )
    .await?;

    diff.record_item(paid);
    diff.record_item(received);
    Ok(())
}
