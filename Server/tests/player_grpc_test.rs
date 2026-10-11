// PlayerService(サインアップ・サインイン)の結合テスト。起動した gRPC サーバーに、tonic の生成クライアントで呼ぶ。
mod common;

use Server::proto::atlas::v1::SignInRequest;
use common::{TestServer, error_reason, prepare_db};
use sqlx::MySqlPool;
use tonic::Code;

#[sqlx::test(migrations = false)]
async fn sign_up_then_sign_in_returns_starter_data(pool: MySqlPool) {
    prepare_db(&pool).await;
    let server = TestServer::start(pool).await;
    let (player_id, token) = server.register_and_issue_token().await;

    let signed_up = server.sign_up(&token, "プレイヤー").await.unwrap();
    assert_eq!(signed_up.player_id, player_id);
    assert_eq!(signed_up.nickname, "プレイヤー");

    let signed_in = server.sign_in(&token).await.unwrap();
    assert_eq!(signed_in.player_id, player_id);
    assert_eq!(signed_in.nickname, "プレイヤー");
    let snapshot = signed_in.snapshot.unwrap();
    // 初期のジェム300個と、スターターの1体(技1つ・パーティの枠1)
    assert_eq!(snapshot.items.len(), 1);
    assert_eq!(snapshot.items[0].item_id, 1);
    assert_eq!(snapshot.items[0].quantity, 300);
    assert_eq!(snapshot.pachimon.len(), 1);
    assert_eq!(snapshot.pachimon[0].pachimon_id, 1);
    assert_eq!(snapshot.pachimon_moves.len(), 1);
    assert_eq!(
        snapshot.pachimon_moves[0].player_pachimon_id,
        snapshot.pachimon[0].player_pachimon_id
    );
    assert_eq!(snapshot.party_slots.len(), 1);
    assert_eq!(snapshot.party_slots[0].slot, 1);
}

#[sqlx::test(migrations = false)]
async fn sign_in_before_sign_up_returns_player_not_found(pool: MySqlPool) {
    prepare_db(&pool).await;
    let server = TestServer::start(pool).await;
    let (_, token) = server.register_and_issue_token().await;

    let status = server.sign_in(&token).await.unwrap_err();
    assert_eq!(status.code(), Code::FailedPrecondition);
    assert_eq!(error_reason(&status).as_deref(), Some("PLAYER_NOT_FOUND"));
}

#[sqlx::test(migrations = false)]
async fn sign_up_twice_returns_existing_player_without_changes(pool: MySqlPool) {
    prepare_db(&pool).await;
    let server = TestServer::start(pool).await;
    let (player_id, token) = server.register_and_issue_token().await;

    server.sign_up(&token, "最初の名前").await.unwrap();
    let second = server.sign_up(&token, "別の名前").await.unwrap();
    assert_eq!(second.player_id, player_id);
    assert_eq!(second.nickname, "最初の名前");

    // 2回目で初期データが重ねて付与されていない
    let snapshot = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    assert_eq!(snapshot.pachimon.len(), 1);
    assert_eq!(snapshot.items[0].quantity, 300);
}

#[sqlx::test(migrations = false)]
async fn sign_up_rejects_invalid_nickname(pool: MySqlPool) {
    prepare_db(&pool).await;
    let server = TestServer::start(pool).await;
    let (_, token) = server.register_and_issue_token().await;

    let too_long = "あ".repeat(51);
    for nickname in ["", too_long.as_str()] {
        let status = server.sign_up(&token, nickname).await.unwrap_err();
        assert_eq!(status.code(), Code::InvalidArgument);
    }
}

#[sqlx::test(migrations = false)]
async fn requests_without_token_are_unauthenticated(pool: MySqlPool) {
    prepare_db(&pool).await;
    let server = TestServer::start(pool).await;

    let status = server.player().sign_in(SignInRequest {}).await.unwrap_err();
    assert_eq!(status.code(), Code::Unauthenticated);
}
