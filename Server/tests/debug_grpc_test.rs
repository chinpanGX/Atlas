// DebugService(開発用のジェム付与・ランダム編成)の結合テスト。起動した gRPC サーバーに、tonic の生成クライアントで呼ぶ。
mod common;

use Server::proto::atlas::v1::debug_service_client::DebugServiceClient;
use Server::proto::atlas::v1::{GrantGemsRequest, RandomizePartyRequest};
use common::{
    TestServer, authorized_with_request_id, game_logs, item_game_logs, prepare_db, with_request_id,
};
use sqlx::MySqlPool;
use tonic::transport::Channel;

const STARTER_GEMS: i32 = 300;
const GRANT_GEMS_AMOUNT: i32 = 450;

async fn start(pool: MySqlPool) -> TestServer {
    prepare_db(&pool).await;
    TestServer::start(pool).await
}

fn client(server: &TestServer) -> DebugServiceClient<Channel> {
    DebugServiceClient::new(server.channel())
}

/// ジェムを付与すると、付与した後の数が items だけに入る。同じ RequestID で送り直しても1回分しか増えない。
#[sqlx::test(migrations = false)]
async fn grant_gems_adds_gems_once_per_request(pool: MySqlPool) {
    let server = start(pool).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let request_id = ulid::Ulid::generate().to_string();

    for _ in 0..2 {
        let diff = client(&server)
            .grant_gems(with_request_id(GrantGemsRequest {}, &token, &request_id))
            .await
            .unwrap()
            .into_inner()
            .player_diff
            .unwrap();
        assert!(diff.pachimon.is_none());
        assert!(diff.party_slots.is_none());
        let items = diff.items.unwrap();
        assert_eq!(items.upserted[0].item_id, 1);
        assert_eq!(items.upserted[0].quantity, STARTER_GEMS + GRANT_GEMS_AMOUNT);
    }

    let snapshot = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    assert_eq!(snapshot.items[0].quantity, STARTER_GEMS + GRANT_GEMS_AMOUNT);

    let logs = item_game_logs(&server.state.pool, &player_id).await;
    assert_eq!(logs.len(), 2);
    assert_eq!(logs[1].delta, i64::from(GRANT_GEMS_AMOUNT));
    assert_eq!(logs[1].after, i64::from(STARTER_GEMS + GRANT_GEMS_AMOUNT));
    assert_eq!(logs[1].reason, "debug");
}

/// ランダム編成で、新しく付与した個体と技、以前の編成の解除と新しい編成が player_diff に入り、全件と一致する。
#[sqlx::test(migrations = false)]
async fn randomize_party_replaces_party_with_new_pachimon(pool: MySqlPool) {
    let server = start(pool).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let before = server.sign_in(&token).await.unwrap().snapshot.unwrap();

    let diff = client(&server)
        .randomize_party(authorized_with_request_id(RandomizePartyRequest {}, &token))
        .await
        .unwrap()
        .into_inner()
        .player_diff
        .unwrap();

    assert!(diff.items.is_none());
    // テストのマスタはパチモンが1種類だけなので、1体だけ付与される
    let pachimon = diff.pachimon.unwrap();
    assert_eq!(pachimon.upserted.len(), 1);
    let moves = diff.pachimon_move_map.unwrap();
    assert!(
        moves
            .upserted
            .iter()
            .all(|m| m.player_pachimon_id == pachimon.upserted[0].player_pachimon_id)
    );
    let slots = diff.party_slots.unwrap();
    let removed: Vec<_> = before
        .party_slots
        .iter()
        .map(|s| s.party_slot_id.clone())
        .collect();
    assert_eq!(slots.removed, removed);
    assert_eq!(slots.upserted.len(), 1);
    assert_eq!(
        slots.upserted[0].player_pachimon_id,
        pachimon.upserted[0].player_pachimon_id
    );

    let after = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    assert_eq!(after.pachimon.len(), before.pachimon.len() + 1);
    assert_eq!(after.party_slots, slots.upserted);

    // サインアップの初期パーティの次に、付与した個体と変更後の編成が残る
    let pachimon_logs = game_logs(&server.state.pool, &player_id, "pachimon").await;
    assert_eq!(pachimon_logs.len(), 2);
    assert_eq!(
        pachimon_logs[1].body["target_id"],
        pachimon.upserted[0].player_pachimon_id
    );
    assert_eq!(pachimon_logs[1].body["reason"], "debug");
    let party_logs = game_logs(&server.state.pool, &player_id, "party").await;
    assert_eq!(party_logs.len(), 2);
    assert_eq!(
        party_logs[1].body["slots"],
        serde_json::json!([{"slot": 1, "player_pachimon_id": pachimon.upserted[0].player_pachimon_id}])
    );
    assert_eq!(party_logs[1].body["reason"], "debug");
}
