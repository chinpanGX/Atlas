// PartyService(パーティの編成・技の付け替え)の結合テスト。起動した gRPC サーバーに、tonic の生成クライアントで呼ぶ。
mod common;

use Server::proto::atlas::v1::party_service_client::PartyServiceClient;
use Server::proto::atlas::v1::{
    PartySlotInput, SetPachimonMoveRequest, SetPachimonMoveResponse, SetPartyRequest,
    SetPartyResponse,
};
use common::{TestServer, authorized_with_request_id, error_reason, prepare_db, with_request_id};
use sqlx::MySqlPool;
use tonic::Code;
use tonic::transport::Channel;

/// スターター(pachimon_id=1)の技グループ1に、付け替えの候補技(move_id=2)を足し、
/// どの技グループにも入っていない技(move_id=3)を作る。サーバーの起動(マスタの読み込み)より前に呼ぶ。
async fn add_moves(pool: &MySqlPool) {
    let queries = [
        "INSERT INTO moves (move_id, name, move_type, category, base_power, accuracy, max_pp) \
         VALUES (2, 'ひっかく', 1, 1, 40, 100, 35)",
        "INSERT INTO moves (move_id, name, move_type, category, base_power, accuracy, max_pp) \
         VALUES (3, 'はかいこうせん', 1, 2, 150, 90, 5)",
        "INSERT INTO move_group_moves (unique_id, group_id, move_id, is_initial) \
         VALUES (2, 1, 2, FALSE)",
    ];
    for query in queries {
        sqlx::query(query).execute(pool).await.unwrap();
    }
}

async fn start(pool: MySqlPool) -> TestServer {
    prepare_db(&pool).await;
    add_moves(&pool).await;
    TestServer::start(pool).await
}

fn client(server: &TestServer) -> PartyServiceClient<Channel> {
    PartyServiceClient::new(server.channel())
}

/// サインインの全件から、スターターの所持個体のIDを取る。
async fn starter_pachimon_id(server: &TestServer, token: &str) -> String {
    let snapshot = server.sign_in(token).await.unwrap().snapshot.unwrap();
    snapshot.pachimon[0].player_pachimon_id.clone()
}

fn party_request(slots: &[(i32, &str)]) -> SetPartyRequest {
    SetPartyRequest {
        party_slots: slots
            .iter()
            .map(|(slot, id)| PartySlotInput {
                slot: *slot,
                player_pachimon_id: (*id).to_string(),
            })
            .collect(),
    }
}

async fn set_party(
    server: &TestServer,
    token: &str,
    slots: &[(i32, &str)],
) -> Result<SetPartyResponse, tonic::Status> {
    client(server)
        .set_party(authorized_with_request_id(party_request(slots), token))
        .await
        .map(tonic::Response::into_inner)
}

async fn set_move(
    server: &TestServer,
    token: &str,
    player_pachimon_id: &str,
    slot: i32,
    move_id: i32,
) -> Result<SetPachimonMoveResponse, tonic::Status> {
    client(server)
        .set_pachimon_move(authorized_with_request_id(
            SetPachimonMoveRequest {
                player_pachimon_id: player_pachimon_id.to_string(),
                slot,
                move_id,
            },
            token,
        ))
        .await
        .map(tonic::Response::into_inner)
}

async fn party_slot_ids(pool: &MySqlPool, player_id: &str) -> Vec<String> {
    sqlx::query_scalar("SELECT party_slot_id FROM player_party_slots WHERE player_id = ?")
        .bind(player_id)
        .fetch_all(pool)
        .await
        .unwrap()
}

/// 編成し直すと、以前の割当が removed に、新しい割当が upserted に入り、ほかの種別は変わらない。
#[sqlx::test(migrations = false)]
async fn set_party_replaces_slots(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    let before = party_slot_ids(&pool, &player_id).await;

    let diff = set_party(&server, &token, &[(3, &pachimon_id)])
        .await
        .unwrap()
        .player_diff
        .unwrap();

    assert!(diff.items.is_none());
    assert!(diff.pachimon.is_none());
    assert!(diff.pachimon_move_map.is_none());
    let slots = diff.party_slots.unwrap();
    assert_eq!(slots.removed, before);
    assert_eq!(slots.upserted.len(), 1);
    assert_eq!(slots.upserted[0].slot, 3);
    assert_eq!(slots.upserted[0].player_pachimon_id, pachimon_id);
    assert_eq!(
        party_slot_ids(&pool, &player_id).await,
        vec![slots.upserted[0].party_slot_id.clone()]
    );
}

/// 同じ RequestID で送り直すと、編成し直さずに保存済みのレスポンスが返る。
#[sqlx::test(migrations = false)]
async fn set_party_resent_with_same_request_id_runs_once(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    let request_id = ulid::Ulid::generate().to_string();

    let mut responses = Vec::new();
    for _ in 0..2 {
        let response = client(&server)
            .set_party(with_request_id(
                party_request(&[(2, &pachimon_id)]),
                &token,
                &request_id,
            ))
            .await
            .unwrap()
            .into_inner();
        responses.push(response.player_diff.unwrap().party_slots.unwrap());
    }

    assert_eq!(
        responses[0].upserted[0].party_slot_id,
        responses[1].upserted[0].party_slot_id
    );
    assert_eq!(
        party_slot_ids(&pool, &player_id).await,
        vec![responses[0].upserted[0].party_slot_id.clone()]
    );
}

/// 人数・枠の番号・重複が規則に合わなければ INVALID_PARTY で、何も変えない。
#[sqlx::test(migrations = false)]
async fn set_party_rejects_invalid_party(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    let before = party_slot_ids(&pool, &player_id).await;

    let seven: Vec<(i32, String)> = (1..=7).map(|slot| (slot, format!("id-{slot}"))).collect();
    let seven: Vec<(i32, &str)> = seven
        .iter()
        .map(|(slot, id)| (*slot, id.as_str()))
        .collect();
    let cases: Vec<Vec<(i32, &str)>> = vec![
        vec![],
        seven,
        vec![(7, &pachimon_id)],
        vec![(1, &pachimon_id), (1, "other")],
        vec![(1, &pachimon_id), (2, &pachimon_id)],
    ];
    for slots in cases {
        let status = set_party(&server, &token, &slots).await.unwrap_err();
        assert_eq!(status.code(), Code::FailedPrecondition);
        assert_eq!(error_reason(&status).as_deref(), Some("INVALID_PARTY"));
    }
    assert_eq!(party_slot_ids(&pool, &player_id).await, before);
}

/// 他のプレイヤーの個体・存在しない個体を指定すると PACHIMON_NOT_OWNED。
#[sqlx::test(migrations = false)]
async fn set_party_rejects_pachimon_not_owned(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;
    let (_, other_token) = server.create_player("ほかのプレイヤー").await;
    let others = starter_pachimon_id(&server, &other_token).await;

    for id in [others.as_str(), "01ARZ3NDEKTSV4RRFFQ69G5FAV"] {
        let status = set_party(&server, &token, &[(1, id)]).await.unwrap_err();
        assert_eq!(error_reason(&status).as_deref(), Some("PACHIMON_NOT_OWNED"));
    }
}

/// 既にある枠を付け替えると、割当の ID はそのままで技が変わり、付け替えた1件だけが返る。
#[sqlx::test(migrations = false)]
async fn set_pachimon_move_keeps_assignment_id(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;
    let snapshot = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    let pachimon_id = snapshot.pachimon[0].player_pachimon_id.clone();
    let before = snapshot.pachimon_moves[0].clone();

    let diff = set_move(&server, &token, &pachimon_id, before.slot, 2)
        .await
        .unwrap()
        .player_diff
        .unwrap();

    assert!(diff.party_slots.is_none());
    let moves = diff.pachimon_move_map.unwrap();
    assert_eq!(moves.upserted.len(), 1);
    assert_eq!(
        moves.upserted[0].player_pachimon_move_id,
        before.player_pachimon_move_id
    );
    assert_eq!(moves.upserted[0].move_id, 2);

    let after = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    assert_eq!(after.pachimon_moves[0].move_id, 2);
}

/// 枠が範囲外・候補技でない・所持個体でないときは、それぞれの業務エラーで、何も変えない。
#[sqlx::test(migrations = false)]
async fn set_pachimon_move_rejects_invalid_requests(pool: MySqlPool) {
    let server = start(pool).await;
    let (_, token) = server.create_player("プレイヤー").await;
    let (_, other_token) = server.create_player("ほかのプレイヤー").await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    let others = starter_pachimon_id(&server, &other_token).await;

    let cases = [
        (pachimon_id.as_str(), 5, 2, "INVALID_MOVE_SLOT"),
        (pachimon_id.as_str(), 1, 3, "MOVE_NOT_LEARNABLE"),
        (others.as_str(), 1, 2, "PACHIMON_NOT_OWNED"),
    ];
    for (id, slot, move_id, reason) in cases {
        let status = set_move(&server, &token, id, slot, move_id)
            .await
            .unwrap_err();
        assert_eq!(status.code(), Code::FailedPrecondition);
        assert_eq!(error_reason(&status).as_deref(), Some(reason));
    }

    let after = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    assert_eq!(after.pachimon_moves.len(), 1);
    assert_eq!(after.pachimon_moves[0].move_id, 1);
}
