// MatchmakingService(待機列への参加・離脱・状況の確認)の結合テスト。起動した gRPC サーバーに、tonic の生成クライアントで呼ぶ。
mod common;

use Server::proto::atlas::v1::matchmaking_service_client::MatchmakingServiceClient;
use Server::proto::atlas::v1::{
    GetQueueStatusRequest, GetQueueStatusResponse, JoinQueueRequest, LeaveQueueRequest, QueueStatus,
};
use Server::service::matchmaking_service::{BATTLE_TOKEN_TTL_SECONDS, BattleTokenClaims};
use chrono::Utc;
use common::{TestServer, authorized, authorized_with_request_id, error_reason, prepare_db};
use jsonwebtoken::{DecodingKey, Validation};
use sqlx::MySqlPool;
use tonic::Code;
use tonic::transport::Channel;

async fn start(pool: MySqlPool) -> TestServer {
    prepare_db(&pool).await;
    TestServer::start(pool).await
}

fn client(server: &TestServer) -> MatchmakingServiceClient<Channel> {
    MatchmakingServiceClient::new(server.channel())
}

async fn join(server: &TestServer, token: &str) -> Result<(), tonic::Status> {
    client(server)
        .join_queue(authorized_with_request_id(JoinQueueRequest {}, token))
        .await
        .map(|_| ())
}

async fn leave(server: &TestServer, token: &str) {
    client(server)
        .leave_queue(authorized(LeaveQueueRequest {}, token))
        .await
        .unwrap();
}

async fn status(server: &TestServer, token: &str) -> GetQueueStatusResponse {
    client(server)
        .get_queue_status(authorized(GetQueueStatusRequest {}, token))
        .await
        .unwrap()
        .into_inner()
}

fn is_waiting(response: &GetQueueStatusResponse) -> bool {
    response.status() == QueueStatus::Waiting
        && response.match_id.is_empty()
        && response.battle_token.is_empty()
}

/// 2人目が参加した時点で成立し、両者に同じ対戦の別々の接続情報が返る。確認し直しても同じものが返る(消えない)。
#[sqlx::test(migrations = false)]
async fn two_players_are_matched(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token_a) = server.create_player("プレイヤーA").await;
    let (_, token_b) = server.create_player("プレイヤーB").await;

    join(&server, &token_a).await.unwrap();
    assert!(is_waiting(&status(&server, &token_a).await));

    join(&server, &token_b).await.unwrap();
    let status_a = status(&server, &token_a).await;
    let status_b = status(&server, &token_b).await;

    assert_eq!(status_a.status(), QueueStatus::Matched);
    assert_eq!(status_b.status(), QueueStatus::Matched);
    assert!(!status_a.match_id.is_empty());
    assert_eq!(status_a.match_id, status_b.match_id);
    assert_eq!(status_a.battle_server, status_b.battle_server);
    assert_ne!(status_a.battle_token, status_b.battle_token);
    assert_eq!(status(&server, &token_a).await, status_a);
}

/// 1人だけなら待機中のまま。二重に参加しても自分自身とはマッチしない。
#[sqlx::test(migrations = false)]
async fn single_player_keeps_waiting(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;

    join(&server, &token).await.unwrap();
    join(&server, &token).await.unwrap();

    assert!(is_waiting(&status(&server, &token).await));
}

/// 離脱した後は、別のプレイヤーが参加してもマッチしない。成立した後に離脱すると、接続情報も消える。
#[sqlx::test(migrations = false)]
async fn leave_queue_removes_player_and_match(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token_a) = server.create_player("プレイヤーA").await;
    let (_, token_b) = server.create_player("プレイヤーB").await;
    let (_, token_c) = server.create_player("プレイヤーC").await;

    join(&server, &token_a).await.unwrap();
    leave(&server, &token_a).await;
    join(&server, &token_b).await.unwrap();
    assert!(is_waiting(&status(&server, &token_a).await));
    assert!(is_waiting(&status(&server, &token_b).await));

    join(&server, &token_c).await.unwrap();
    assert_eq!(
        status(&server, &token_b).await.status(),
        QueueStatus::Matched
    );
    leave(&server, &token_b).await;
    assert!(is_waiting(&status(&server, &token_b).await));
    // 待機列にいなくても成功する
    leave(&server, &token_b).await;
}

/// 前のマッチの接続情報が残ったまま参加し直すと、それを捨てて新しく待機する。
#[sqlx::test(migrations = false)]
async fn join_again_discards_previous_match(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token_a) = server.create_player("プレイヤーA").await;
    let (_, token_b) = server.create_player("プレイヤーB").await;
    join(&server, &token_a).await.unwrap();
    join(&server, &token_b).await.unwrap();

    join(&server, &token_a).await.unwrap();

    assert!(is_waiting(&status(&server, &token_a).await));
    assert_eq!(
        status(&server, &token_b).await.status(),
        QueueStatus::Matched
    );
}

/// battle_token は BATTLE_TOKEN_SECRET で検証でき、対戦・プレイヤー・有効期限が入っている。
#[sqlx::test(migrations = false)]
async fn battle_token_is_valid(pool: MySqlPool) {
    let server = start(pool).await;
    let (player_a, token_a) = server.create_player("プレイヤーA").await;
    let (player_b, token_b) = server.create_player("プレイヤーB").await;
    join(&server, &token_a).await.unwrap();
    join(&server, &token_b).await.unwrap();

    let secret = std::env::var("BATTLE_TOKEN_SECRET").unwrap();
    let mut validation = Validation::default(); // HS256
    validation.leeway = 0;

    for (token, player_id) in [(&token_a, &player_a), (&token_b, &player_b)] {
        let response = status(&server, token).await;
        let claims = jsonwebtoken::decode::<BattleTokenClaims>(
            &response.battle_token,
            &DecodingKey::from_secret(secret.as_bytes()),
            &validation,
        )
        .unwrap()
        .claims;

        assert_eq!(claims.match_id, response.match_id);
        assert_eq!(&claims.player_id, player_id);
        let remaining = claims.exp - Utc::now().timestamp();
        assert!(remaining > 0 && remaining <= BATTLE_TOKEN_TTL_SECONDS);
        assert!(
            jsonwebtoken::decode::<BattleTokenClaims>(
                &response.battle_token,
                &DecodingKey::from_secret(b"wrong-secret"),
                &validation,
            )
            .is_err()
        );
    }
}

/// パーティが空なら PARTY_EMPTY で、待機列に入らない。
#[sqlx::test(migrations = false)]
async fn join_with_empty_party_is_rejected(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let (_, other_token) = server.create_player("ほかのプレイヤー").await;
    sqlx::query("DELETE FROM player_party_slots WHERE player_id = ?")
        .bind(&player_id)
        .execute(&pool)
        .await
        .unwrap();

    let status_code = join(&server, &token).await.unwrap_err();
    assert_eq!(status_code.code(), Code::FailedPrecondition);
    assert_eq!(error_reason(&status_code).as_deref(), Some("PARTY_EMPTY"));

    // 待機列に入っていないので、ほかのプレイヤーが参加しても成立しない
    join(&server, &other_token).await.unwrap();
    assert!(is_waiting(&status(&server, &other_token).await));
}
