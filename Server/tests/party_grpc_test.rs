// PartyService(パーティの編成・技の付け替え・ステータスポイントの割り振り)の結合テスト。
// 起動した gRPC サーバーに、tonic の生成クライアントで呼ぶ。
mod common;

use Server::proto::atlas::v1::party_service_client::PartyServiceClient;
use Server::proto::atlas::v1::{
    PartySlotInput, SetPachimonMoveRequest, SetPachimonMoveResponse, SetPachimonStatPointsRequest,
    SetPachimonStatPointsResponse, SetPartyRequest, SetPartyResponse, StatPoints,
};
use common::{
    TRAINING_TICKET_ITEM_ID, TestServer, authorized_with_request_id, error_reason, game_logs,
    item_game_logs, item_quantity, prepare_db, set_item_quantity, with_request_id,
};
use serde_json::json;
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

fn stat_points_request(player_pachimon_id: &str, points: StatPoints) -> SetPachimonStatPointsRequest {
    SetPachimonStatPointsRequest {
        player_pachimon_id: player_pachimon_id.to_string(),
        stat_points: Some(points),
    }
}

async fn set_stat_points(
    server: &TestServer,
    token: &str,
    player_pachimon_id: &str,
    points: StatPoints,
) -> Result<SetPachimonStatPointsResponse, tonic::Status> {
    client(server)
        .set_pachimon_stat_points(authorized_with_request_id(
            stat_points_request(player_pachimon_id, points),
            token,
        ))
        .await
        .map(tonic::Response::into_inner)
}

/// サインインの全件から、スターターのステータスポイントを取る。
async fn starter_stat_points(server: &TestServer, token: &str) -> StatPoints {
    let snapshot = server.sign_in(token).await.unwrap().snapshot.unwrap();
    snapshot.pachimon[0].stat_points.unwrap()
}

fn points(hp: i32, atk: i32, speed: i32) -> StatPoints {
    StatPoints {
        hp,
        atk,
        speed,
        ..StatPoints::default()
    }
}

async fn tickets(pool: &MySqlPool, player_id: &str) -> i32 {
    item_quantity(pool, player_id, TRAINING_TICKET_ITEM_ID).await
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

    // サインアップの初期パーティの次に、変更後の編成が残る
    let logs = game_logs(&pool, &player_id, "party").await;
    assert_eq!(logs.len(), 2);
    assert_eq!(logs[1].method, "atlas.v1.PartyService/SetParty");
    assert_eq!(
        logs[1].body,
        json!({"slots": [{"slot": 3, "player_pachimon_id": pachimon_id}], "reason": "set_party"})
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

/// 既にある枠を付け替えると、割当の ID はそのままで技が変わり、付け替えた1件と、使った後の育成チケットが返る。
#[sqlx::test(migrations = false)]
async fn set_pachimon_move_keeps_assignment_id(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 7).await;
    let snapshot = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    let pachimon_id = snapshot.pachimon[0].player_pachimon_id.clone();
    let before = snapshot.pachimon_moves[0].clone();

    let diff = set_move(&server, &token, &pachimon_id, before.slot, 2)
        .await
        .unwrap()
        .player_diff
        .unwrap();

    assert!(diff.party_slots.is_none());
    let items = diff.items.unwrap();
    assert_eq!(items.upserted.len(), 1);
    assert_eq!(items.upserted[0].item_id, TRAINING_TICKET_ITEM_ID);
    assert_eq!(items.upserted[0].quantity, 2);
    assert_eq!(tickets(&pool, &player_id).await, 2);
    let moves = diff.pachimon_move_map.unwrap();
    assert_eq!(moves.upserted.len(), 1);
    assert_eq!(
        moves.upserted[0].player_pachimon_move_id,
        before.player_pachimon_move_id
    );
    assert_eq!(moves.upserted[0].move_id, 2);

    let after = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    assert_eq!(after.pachimon_moves[0].move_id, 2);

    let logs = game_logs(&server.state.pool, &player_id, "pachimon_move").await;
    assert_eq!(logs.len(), 1);
    assert_eq!(logs[0].method, "atlas.v1.PartyService/SetPachimonMove");
    assert_eq!(
        logs[0].body,
        json!({
            "target_id": pachimon_id,
            "slot": before.slot,
            "before_move_id": before.move_id,
            "after_move_id": 2,
            "reason": "set_pachimon_move",
        })
    );
    let item_logs = item_game_logs(&pool, &player_id).await;
    let ticket_log = item_logs.last().unwrap();
    assert_eq!(ticket_log.method, "atlas.v1.PartyService/SetPachimonMove");
    assert_eq!(ticket_log.item_id, TRAINING_TICKET_ITEM_ID.to_string());
    assert_eq!((ticket_log.delta, ticket_log.after), (-5, 2));
    assert_eq!(ticket_log.reason, "set_pachimon_move");
}

/// 空いている枠に技を覚えると、ゲームログの前の技は null になる。
#[sqlx::test(migrations = false)]
async fn set_pachimon_move_to_empty_slot_logs_no_previous_move(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 5).await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;

    // スターターは初期技を1つだけ覚えているので、枠2は空いている
    set_move(&server, &token, &pachimon_id, 2, 2).await.unwrap();

    let logs = game_logs(&server.state.pool, &player_id, "pachimon_move").await;
    assert_eq!(logs.len(), 1);
    assert_eq!(logs[0].body["slot"], 2);
    assert_eq!(logs[0].body["before_move_id"], serde_json::Value::Null);
    assert_eq!(logs[0].body["after_move_id"], 2);
}

/// 枠が範囲外・候補技でない・所持個体でない・同じ技に付け替える・別の枠の技をもう1つ覚えるときは、
/// それぞれの業務エラーで、技も育成チケットも変えない。
#[sqlx::test(migrations = false)]
async fn set_pachimon_move_rejects_invalid_requests(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let (_, other_token) = server.create_player("ほかのプレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 10).await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    let others = starter_pachimon_id(&server, &other_token).await;

    // スターターは枠1に move_id=1 を覚えている
    let cases = [
        (pachimon_id.as_str(), 5, 2, "INVALID_MOVE_SLOT"),
        (pachimon_id.as_str(), 1, 3, "MOVE_NOT_LEARNABLE"),
        (others.as_str(), 1, 2, "PACHIMON_NOT_OWNED"),
        (pachimon_id.as_str(), 1, 1, "MOVE_UNCHANGED"),
        (pachimon_id.as_str(), 2, 1, "MOVE_ALREADY_LEARNED"),
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
    assert_eq!(tickets(&pool, &player_id).await, 10);
}

/// 育成チケットが5枚に足りなければ INSUFFICIENT_TRAINING_TICKETS で、技もチケットも変えない。
#[sqlx::test(migrations = false)]
async fn set_pachimon_move_rejects_insufficient_tickets(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 4).await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;

    let status = set_move(&server, &token, &pachimon_id, 1, 2)
        .await
        .unwrap_err();

    assert_eq!(
        error_reason(&status).as_deref(),
        Some("INSUFFICIENT_TRAINING_TICKETS")
    );
    let after = server.sign_in(&token).await.unwrap().snapshot.unwrap();
    assert_eq!(after.pachimon_moves[0].move_id, 1);
    assert_eq!(tickets(&pool, &player_id).await, 4);
}

/// 同じ RequestID で送り直しても、育成チケットは1回分しか使わない。
#[sqlx::test(migrations = false)]
async fn set_pachimon_move_resent_with_same_request_id_runs_once(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 10).await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    let request_id = ulid::Ulid::generate().to_string();

    for _ in 0..2 {
        client(&server)
            .set_pachimon_move(with_request_id(
                SetPachimonMoveRequest {
                    player_pachimon_id: pachimon_id.clone(),
                    slot: 1,
                    move_id: 2,
                },
                &token,
                &request_id,
            ))
            .await
            .unwrap();
    }

    assert_eq!(tickets(&pool, &player_id).await, 5);
}

/// 入手したときは全能力0で、増やしたポイントの数だけ育成チケットを使う。変えた個体と使った後のチケットが返り、
/// 前後の割り振りがゲームログに残る。
#[sqlx::test(migrations = false)]
async fn set_stat_points_consumes_tickets_for_increase(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 70).await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    assert_eq!(starter_stat_points(&server, &token).await, StatPoints::default());

    let diff = set_stat_points(&server, &token, &pachimon_id, points(32, 16, 16))
        .await
        .unwrap()
        .player_diff
        .unwrap();

    assert!(diff.pachimon_move_map.is_none());
    assert!(diff.party_slots.is_none());
    assert_eq!(diff.items.unwrap().upserted[0].quantity, 6);
    let pachimon = diff.pachimon.unwrap();
    assert_eq!(pachimon.upserted.len(), 1);
    assert_eq!(pachimon.upserted[0].player_pachimon_id, pachimon_id);
    assert_eq!(pachimon.upserted[0].stat_points, Some(points(32, 16, 16)));
    assert_eq!(tickets(&pool, &player_id).await, 6);
    assert_eq!(starter_stat_points(&server, &token).await, points(32, 16, 16));

    let logs = game_logs(&pool, &player_id, "pachimon_stat_points").await;
    assert_eq!(logs.len(), 1);
    assert_eq!(logs[0].method, "atlas.v1.PartyService/SetPachimonStatPoints");
    let zero = json!({"hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 0});
    assert_eq!(
        logs[0].body,
        json!({
            "target_id": pachimon_id,
            "before_stat_points": zero,
            "after_stat_points": {"hp": 32, "atk": 16, "def": 0, "spatk": 0, "spdef": 0, "speed": 16},
            "reason": "set_pachimon_stat_points",
        })
    );
    let ticket_log = item_game_logs(&pool, &player_id).await.pop().unwrap();
    assert_eq!((ticket_log.delta, ticket_log.after), (-64, 6));
    assert_eq!(ticket_log.reason, "set_pachimon_stat_points");
}

/// 振り直しでは増やした分だけを数え、減らすのは無料。減らすだけならチケットは返らない。
#[sqlx::test(migrations = false)]
async fn set_stat_points_counts_only_increases(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 40).await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    set_stat_points(&server, &token, &pachimon_id, points(0, 30, 0))
        .await
        .unwrap();

    // こうげきから10外してすばやさに10足すと10枚
    set_stat_points(&server, &token, &pachimon_id, points(0, 20, 10))
        .await
        .unwrap();
    assert_eq!(tickets(&pool, &player_id).await, 0);

    // 減らすだけなら0枚で、items は返らない
    let diff = set_stat_points(&server, &token, &pachimon_id, points(0, 5, 0))
        .await
        .unwrap()
        .player_diff
        .unwrap();
    assert!(diff.items.is_none());
    assert_eq!(diff.pachimon.unwrap().upserted[0].stat_points, Some(points(0, 5, 0)));
    assert_eq!(starter_stat_points(&server, &token).await, points(0, 5, 0));
}

/// 今と同じ割り振りを送ると、チケットを使わず何も変えず、player_diff は無い。
#[sqlx::test(migrations = false)]
async fn set_stat_points_unchanged_does_nothing(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;

    let response = set_stat_points(&server, &token, &pachimon_id, StatPoints::default())
        .await
        .unwrap();

    assert!(response.player_diff.is_none());
    assert!(
        game_logs(&pool, &player_id, "pachimon_stat_points")
            .await
            .is_empty()
    );
}

/// 負の値・合計64超え・値なし・所持個体でない・チケット不足は、それぞれの業務エラーで、何も変えない。
#[sqlx::test(migrations = false)]
async fn set_stat_points_rejects_invalid_requests(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    let (_, other_token) = server.create_player("ほかのプレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 10).await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    let others = starter_pachimon_id(&server, &other_token).await;

    let cases = [
        (pachimon_id.as_str(), Some(points(-1, 0, 0)), "INVALID_STAT_POINTS"),
        (pachimon_id.as_str(), Some(points(32, 16, 17)), "INVALID_STAT_POINTS"),
        (pachimon_id.as_str(), None, "INVALID_STAT_POINTS"),
        (others.as_str(), Some(points(1, 0, 0)), "PACHIMON_NOT_OWNED"),
        (pachimon_id.as_str(), Some(points(11, 0, 0)), "INSUFFICIENT_TRAINING_TICKETS"),
    ];
    for (id, stat_points, reason) in cases {
        let status = client(&server)
            .set_pachimon_stat_points(authorized_with_request_id(
                SetPachimonStatPointsRequest {
                    player_pachimon_id: id.to_string(),
                    stat_points,
                },
                &token,
            ))
            .await
            .unwrap_err();
        assert_eq!(status.code(), Code::FailedPrecondition);
        assert_eq!(error_reason(&status).as_deref(), Some(reason));
    }

    assert_eq!(starter_stat_points(&server, &token).await, StatPoints::default());
    assert_eq!(tickets(&pool, &player_id).await, 10);
}

/// 同じ RequestID で送り直しても、育成チケットは1回分しか使わない。
#[sqlx::test(migrations = false)]
async fn set_stat_points_resent_with_same_request_id_runs_once(pool: MySqlPool) {
    let server = start(pool.clone()).await;
    let (player_id, token) = server.create_player("プレイヤー").await;
    set_item_quantity(&pool, &player_id, TRAINING_TICKET_ITEM_ID, 10).await;
    let pachimon_id = starter_pachimon_id(&server, &token).await;
    let request_id = ulid::Ulid::generate().to_string();

    for _ in 0..2 {
        client(&server)
            .set_pachimon_stat_points(with_request_id(
                stat_points_request(&pachimon_id, points(4, 0, 0)),
                &token,
                &request_id,
            ))
            .await
            .unwrap();
    }

    assert_eq!(tickets(&pool, &player_id).await, 6);
    assert_eq!(starter_stat_points(&server, &token).await, points(4, 0, 0));
}
