// ShopService(ショップの商品一覧・購入)の結合テスト。起動した gRPC サーバーに、tonic の生成クライアントで呼ぶ。
mod common;

use Server::proto::atlas::v1::shop_service_client::ShopServiceClient;
use Server::proto::atlas::v1::{
    ListProductsRequest, PurchaseRequest, PurchaseResponse, ShopProduct,
};
use common::{
    TRAINING_TICKET_ITEM_ID, TestServer, authorized, authorized_with_request_id, error_reason,
    item_game_logs, item_quantity, prepare_db, set_item_quantity, with_request_id,
};
use sqlx::MySqlPool;
use tonic::Code;
use tonic::transport::Channel;

const GEM_ITEM_ID: i32 = 1;
/// サインアップで付与されるジェム。
const STARTER_GEMS: i32 = 300;

/// 育成チケット10枚 = ジェム50、100枚 = 450 の2つの商品を入れる。サーバーの起動(マスタの読み込み)より前に呼ぶ。
async fn add_products(pool: &MySqlPool) {
    let queries = [
        "INSERT INTO shop_products (shop_product_id, item_id, amount, cost_item_id, cost_amount) \
         VALUES (2, 2, 100, 1, 450)",
        "INSERT INTO shop_products (shop_product_id, item_id, amount, cost_item_id, cost_amount) \
         VALUES (1, 2, 10, 1, 50)",
    ];
    for query in queries {
        sqlx::query(query).execute(pool).await.unwrap();
    }
}

async fn start(pool: MySqlPool) -> TestServer {
    prepare_db(&pool).await;
    add_products(&pool).await;
    TestServer::start(pool).await
}

fn client(server: &TestServer) -> ShopServiceClient<Channel> {
    ShopServiceClient::new(server.channel())
}

async fn purchase(
    server: &TestServer,
    token: &str,
    shop_product_id: i32,
) -> Result<PurchaseResponse, tonic::Status> {
    client(server)
        .purchase(authorized_with_request_id(
            PurchaseRequest { shop_product_id },
            token,
        ))
        .await
        .map(tonic::Response::into_inner)
}

/// 商品が shop_product_id の順にすべて返る。
#[sqlx::test(migrations = false)]
async fn list_products_returns_all_products(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;

    let products = client(&server)
        .list_products(authorized(ListProductsRequest {}, &token))
        .await
        .unwrap()
        .into_inner()
        .products;

    assert_eq!(
        products,
        vec![
            ShopProduct {
                shop_product_id: 1,
                item_id: TRAINING_TICKET_ITEM_ID,
                amount: 10,
                cost_item_id: GEM_ITEM_ID,
                cost_amount: 50,
            },
            ShopProduct {
                shop_product_id: 2,
                item_id: TRAINING_TICKET_ITEM_ID,
                amount: 100,
                cost_item_id: GEM_ITEM_ID,
                cost_amount: 450,
            },
        ]
    );
}

/// 購入するとジェムが減って育成チケットが増え、両方が items に入り、両方の増減がゲームログに残る。
#[sqlx::test(migrations = false)]
async fn purchase_pays_gems_and_receives_item(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 3).await;

    let diff = purchase(&server, &token, 1)
        .await
        .unwrap()
        .player_diff
        .unwrap();

    assert!(diff.pachimon.is_none());
    assert!(diff.pachimon_move_map.is_none());
    assert!(diff.party_slots.is_none());
    let items: Vec<(i32, i32)> = diff
        .items
        .unwrap()
        .upserted
        .iter()
        .map(|item| (item.item_id, item.quantity))
        .collect();
    assert_eq!(
        items,
        vec![
            (GEM_ITEM_ID, STARTER_GEMS - 50),
            (TRAINING_TICKET_ITEM_ID, 13)
        ]
    );
    assert_eq!(
        item_quantity(&pool, &player_id, GEM_ITEM_ID).await,
        STARTER_GEMS - 50
    );
    assert_eq!(
        item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID).await,
        13
    );

    let logs = item_game_logs(&pool, &player_id).await;
    let purchase_logs: Vec<_> = logs
        .iter()
        .filter(|log| log.reason == "shop_purchase")
        .map(|log| (log.item_id.as_str(), log.delta, log.after))
        .collect();
    assert_eq!(
        purchase_logs,
        vec![
            ("1", -50, i64::from(STARTER_GEMS - 50)),
            ("2", 10, 13)
        ]
    );
    assert!(
        logs.iter()
            .filter(|log| log.reason == "shop_purchase")
            .all(|log| log.method == "atlas.v1.ShopService/Purchase")
    );
}

/// 育成チケットを1枚も持っていなくても、購入すれば行ができる。
#[sqlx::test(migrations = false)]
async fn purchase_without_owned_item_creates_it(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;

    purchase(&server, &token, 1).await.unwrap();

    assert_eq!(
        item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID).await,
        10
    );
}

/// 商品が無ければ SHOP_PRODUCT_NOT_FOUND、ジェムが足りなければ INSUFFICIENT_GEMS で、何も変えない。
#[sqlx::test(migrations = false)]
async fn purchase_rejects_unknown_product_and_insufficient_gems(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;

    for (shop_product_id, reason) in [(99, "SHOP_PRODUCT_NOT_FOUND"), (2, "INSUFFICIENT_GEMS")] {
        let status = purchase(&server, &token, shop_product_id)
            .await
            .unwrap_err();
        assert_eq!(status.code(), Code::FailedPrecondition);
        assert_eq!(error_reason(&status).as_deref(), Some(reason));
    }

    assert_eq!(
        item_quantity(&pool, &player_id, GEM_ITEM_ID).await,
        STARTER_GEMS
    );
    assert_eq!(
        item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID).await,
        0
    );
}

/// 同じ RequestID で送り直しても、1回分しか購入しない。
#[sqlx::test(migrations = false)]
async fn purchase_resent_with_same_request_id_runs_once(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let request_id = ulid::Ulid::generate().to_string();

    for _ in 0..2 {
        client(&server)
            .purchase(with_request_id(
                PurchaseRequest { shop_product_id: 1 },
                &token,
                &request_id,
            ))
            .await
            .unwrap();
    }

    assert_eq!(
        item_quantity(&pool, &player_id, GEM_ITEM_ID).await,
        STARTER_GEMS - 50
    );
    assert_eq!(
        item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID).await,
        10
    );
}
