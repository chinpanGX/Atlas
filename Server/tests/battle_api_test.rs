// tests/battle_api_test.rs
//
// POST /battle/queue, DELETE /battle/queue, GET /battle/queue/status (マッチング)のテスト。
// - test_two_players_matched: 2人がPOSTすると2人目の時点でペア成立し、両者のstatusがmatchedになることを確認。
//   同一matchIdであること、取得後のエントリ削除(2回目はwaiting)も確認
// - test_single_player_waiting: 1人だけの場合はwaitingのままであることを確認
// - test_leave_queue: DELETEで待機列から抜けた後は、別プレイヤーが参加してもマッチしないことを確認
// - test_battle_token_valid: battleTokenがBATTLE_TOKEN_SECRETでdecodeでき、claims・有効期限が正しいことを確認
// - test_join_queue_empty_party: パーティ未編成での参加が400になることを確認
// - test_join_queue_unauthenticated: 未認証での参加が401になることを確認
// - test_match_creates_battle_match_row: ペア成立時にbattle_matchesへin_progressの行が作られ、
//   先に待っていた側がplayer1になることを確認
//
// POST /internal/battle/result (BattleServerからの対戦結果報告)のテスト。
// - test_report_result_success: battle_matchesのfinished更新・battle_turnsの保存・勝者へのgems加算を確認
// - test_report_result_swapped_player_order: BattleServer側のplayer1/player2がマッチ成立時と逆でも、
//   選出がIDで突き合わされて正しい列に保存されることを確認
// - test_report_result_opponent_never_joined: 相手不参加(選出が空配列・player2Idが空文字・turnsが空)を
//   正常に受け付けることを確認
// - test_report_result_wrong_secret: X-Internal-Secretの不一致・欠落で401になり、何も変更されないことを確認
// - test_report_result_unknown_match: 存在しないmatchIdで404になることを確認
// - test_report_result_non_participant: 参加者でないwinnerId/ターンのplayerIdで400になることを確認
// - test_report_result_twice_conflict: 二重報告が409になり、gems・ターンが二重に記録されないことを確認
// - test_report_result_no_winner: 勝者なし(winnerIdが空文字)の報告でabortedになり、gemsが付与されないことを確認
// - test_report_result_after_no_winner_conflict: 勝者なしで報告済みの対戦への再報告が409になることを確認
//
// 結果報告が届かない対戦の後始末(battle_service::abort_stale_matches)のテスト。
// - test_abort_stale_matches: 一定時間を過ぎたin_progressだけがabortedになることを確認
// - test_report_result_after_stale_abort: 打ち切り後に届いた結果報告は受け付け、finishedへ上書きしてgemsを付与することを確認
//
// POST /internal/battle/loadouts (BattleServerが選出個体の所持データを取得する)のテスト。
// - test_battle_loadouts_success: 指定した順番で、パチモンID・努力値・技(slot順)が返ることを確認
// - test_battle_loadouts_not_owned: 他プレイヤーの個体・存在しないIDを含むと404になることを確認
// - test_battle_loadouts_wrong_secret: X-Internal-Secretの不一致・欠落で401になることを確認
use axum::{
    Router,
    body::Body,
    http::{Request, StatusCode, header},
};
use chrono::Utc;
use jsonwebtoken::{DecodingKey, Validation};
use serde_json::{Value, json};
use sqlx::MySqlPool;
use tower::ServiceExt;

use Server::routes::create_router;
use Server::service::battle_service::{
    BATTLE_WIN_REWARD_GEMS, STALE_MATCH_TIMEOUT, abort_stale_matches,
};
use Server::service::matchmaking_service::{BATTLE_TOKEN_TTL_SECONDS, BattleTokenClaims};
use Server::state::AppState;

async fn register_and_authenticate(app: Router, secret_key: &str) -> String {
    let register_response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/devices")
                .header("Content-Type", "application/json")
                .body(Body::from(json!({ "secretKey": secret_key }).to_string()))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(register_response.status(), StatusCode::OK);
    let device_id = json_body(register_response).await["deviceId"]
        .as_str()
        .unwrap()
        .to_string();

    let auth_response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/devices/authenticate")
                .header("Content-Type", "application/json")
                .body(Body::from(
                    json!({ "deviceId": device_id, "secretKey": secret_key }).to_string(),
                ))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(auth_response.status(), StatusCode::OK);
    json_body(auth_response).await["accessToken"]
        .as_str()
        .unwrap()
        .to_string()
}

async fn json_body(response: axum::response::Response) -> Value {
    let body = axum::body::to_bytes(response.into_body(), usize::MAX)
        .await
        .unwrap();
    serde_json::from_slice(&body).unwrap()
}

/// signup時に初期パーティ(pachimon_id=1を1体)が付与されるよう、必要なマスタを直接DBへ投入する。
/// `AppState::from_pool`(マスタのメモリ読み込み)より前に呼ぶ必要がある。
async fn seed_test_master_data(pool: &MySqlPool) {
    let queries = [
        "INSERT INTO items (item_id, name) VALUES (1, 'ジェム')",
        "INSERT INTO move_groups (move_group_id, name) VALUES (1, 'グループ1')",
        "INSERT INTO moves (move_id, name, move_type, category, base_power, accuracy, max_pp) \
         VALUES (1, 'たいあたり', 1, 1, 40, 100, 35)",
        "INSERT INTO move_group_moves (unique_id, group_id, move_id, is_initial) \
         VALUES (1, 1, 1, TRUE)",
        "INSERT INTO pachimon \
            (pachimon_id, name, primary_type, secondary_type, base_hp, base_atk, base_def, \
             base_spatk, base_spdef, base_speed, rarity, move_group_id) \
         VALUES (1, 'テストモン', 1, 0, 50, 50, 50, 50, 50, 50, 4, 1)",
        "INSERT INTO starter_party_slots (slot_no, pachimon_id) VALUES (1, 1)",
    ];
    for query in queries {
        sqlx::query(query).execute(pool).await.unwrap();
    }
}

/// デバイス登録〜signupまで行い、`(access_token, player_id)`を返す。
async fn create_player(app: Router, secret_key: &str, nickname: &str) -> (String, String) {
    let access_token = register_and_authenticate(app.clone(), secret_key).await;

    let signup_response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/signup")
                .header("Content-Type", "application/json")
                .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
                .body(Body::from(json!({ "nickname": nickname }).to_string()))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(signup_response.status(), StatusCode::OK);

    let sign_in_response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/sign-in")
                .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(sign_in_response.status(), StatusCode::OK);
    let player_id = json_body(sign_in_response).await["playerId"]
        .as_str()
        .unwrap()
        .to_string();

    (access_token, player_id)
}

async fn send(
    app: Router,
    method: &str,
    uri: &str,
    access_token: Option<&str>,
) -> axum::response::Response {
    let mut builder = Request::builder().method(method).uri(uri);
    if let Some(token) = access_token {
        builder = builder.header(header::AUTHORIZATION, format!("Bearer {token}"));
    }

    app.oneshot(builder.body(Body::empty()).unwrap())
        .await
        .unwrap()
}

async fn join_queue(app: Router, access_token: &str) -> axum::response::Response {
    send(app, "POST", "/battle/queue", Some(access_token)).await
}

async fn leave_queue(app: Router, access_token: &str) -> axum::response::Response {
    send(app, "DELETE", "/battle/queue", Some(access_token)).await
}

async fn queue_status(app: Router, access_token: &str) -> Value {
    let response = send(app, "GET", "/battle/queue/status", Some(access_token)).await;
    assert_eq!(response.status(), StatusCode::OK);
    json_body(response).await
}

async fn setup_app(pool: MySqlPool) -> Router {
    seed_test_master_data(&pool).await;
    create_router(AppState::from_pool(pool).await)
}

#[sqlx::test]
async fn test_two_players_matched(pool: MySqlPool) {
    let app = setup_app(pool).await;
    let (token_a, _) = create_player(app.clone(), "secret-a", "プレイヤーA").await;
    let (token_b, _) = create_player(app.clone(), "secret-b", "プレイヤーB").await;

    assert_eq!(
        join_queue(app.clone(), &token_a).await.status(),
        StatusCode::OK
    );
    assert_eq!(
        queue_status(app.clone(), &token_a).await["status"],
        "waiting"
    );

    assert_eq!(
        join_queue(app.clone(), &token_b).await.status(),
        StatusCode::OK
    );

    let status_a = queue_status(app.clone(), &token_a).await;
    let status_b = queue_status(app.clone(), &token_b).await;
    assert_eq!(status_a["status"], "matched");
    assert_eq!(status_b["status"], "matched");
    assert_eq!(status_a["matchId"], status_b["matchId"]);
    assert!(!status_a["matchId"].as_str().unwrap().is_empty());
    assert_eq!(status_a["battleServer"], status_b["battleServer"]);
    assert_ne!(status_a["battleToken"], status_b["battleToken"]);

    // 取得済みのマッチ結果は削除される
    assert_eq!(
        queue_status(app.clone(), &token_a).await["status"],
        "waiting"
    );
}

#[sqlx::test]
async fn test_single_player_waiting(pool: MySqlPool) {
    let app = setup_app(pool).await;
    let (token_a, _) = create_player(app.clone(), "secret-a", "プレイヤーA").await;

    assert_eq!(
        join_queue(app.clone(), &token_a).await.status(),
        StatusCode::OK
    );
    // 二重参加しても自分自身とはマッチしない
    assert_eq!(
        join_queue(app.clone(), &token_a).await.status(),
        StatusCode::OK
    );

    let status = queue_status(app.clone(), &token_a).await;
    assert_eq!(status["status"], "waiting");
    assert_eq!(status["matchId"], "");
    assert_eq!(status["battleToken"], "");
}

#[sqlx::test]
async fn test_leave_queue(pool: MySqlPool) {
    let app = setup_app(pool).await;
    let (token_a, _) = create_player(app.clone(), "secret-a", "プレイヤーA").await;
    let (token_b, _) = create_player(app.clone(), "secret-b", "プレイヤーB").await;

    assert_eq!(
        join_queue(app.clone(), &token_a).await.status(),
        StatusCode::OK
    );
    assert_eq!(
        leave_queue(app.clone(), &token_a).await.status(),
        StatusCode::OK
    );

    // Aは離脱済みのため、Bが参加してもペアは成立しない
    assert_eq!(
        join_queue(app.clone(), &token_b).await.status(),
        StatusCode::OK
    );
    assert_eq!(
        queue_status(app.clone(), &token_a).await["status"],
        "waiting"
    );
    assert_eq!(
        queue_status(app.clone(), &token_b).await["status"],
        "waiting"
    );
}

#[sqlx::test]
async fn test_battle_token_valid(pool: MySqlPool) {
    let app = setup_app(pool).await;
    let (token_a, player_a) = create_player(app.clone(), "secret-a", "プレイヤーA").await;
    let (token_b, player_b) = create_player(app.clone(), "secret-b", "プレイヤーB").await;

    join_queue(app.clone(), &token_a).await;
    join_queue(app.clone(), &token_b).await;

    let secret = std::env::var("BATTLE_TOKEN_SECRET").unwrap();
    let mut validation = Validation::default(); // HS256
    validation.leeway = 0;

    for (access_token, player_id) in [(&token_a, &player_a), (&token_b, &player_b)] {
        let status = queue_status(app.clone(), access_token).await;
        let claims = jsonwebtoken::decode::<BattleTokenClaims>(
            status["battleToken"].as_str().unwrap(),
            &DecodingKey::from_secret(secret.as_bytes()),
            &validation,
        )
        .unwrap()
        .claims;

        assert_eq!(claims.match_id, status["matchId"].as_str().unwrap());
        assert_eq!(&claims.player_id, player_id);
        let remaining = claims.exp - Utc::now().timestamp();
        assert!(remaining > 0 && remaining <= BATTLE_TOKEN_TTL_SECONDS);
    }

    // 別のシークレットでは検証に失敗する
    join_queue(app.clone(), &token_a).await;
    join_queue(app.clone(), &token_b).await;
    let status = queue_status(app.clone(), &token_a).await;
    assert!(
        jsonwebtoken::decode::<BattleTokenClaims>(
            status["battleToken"].as_str().unwrap(),
            &DecodingKey::from_secret(b"wrong-secret"),
            &validation,
        )
        .is_err()
    );
}

#[sqlx::test]
async fn test_join_queue_empty_party(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let (token_a, player_a) = create_player(app.clone(), "secret-a", "プレイヤーA").await;
    sqlx::query("DELETE FROM player_party_slots WHERE player_id = ?")
        .bind(&player_a)
        .execute(&pool)
        .await
        .unwrap();

    assert_eq!(
        join_queue(app.clone(), &token_a).await.status(),
        StatusCode::BAD_REQUEST
    );
}

#[sqlx::test]
async fn test_join_queue_unauthenticated(pool: MySqlPool) {
    let app = setup_app(pool).await;

    let response = send(app, "POST", "/battle/queue", None).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

#[sqlx::test]
async fn test_match_creates_battle_match_row(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app).await;

    let (player1_id, player2_id, status): (String, String, String) = sqlx::query_as(
        "SELECT player1_id, player2_id, status FROM battle_matches WHERE match_id = ? \
         AND started_at IS NOT NULL AND ended_at IS NULL",
    )
    .bind(&m.match_id)
    .fetch_one(&pool)
    .await
    .unwrap();
    assert_eq!(player1_id, m.player_a);
    assert_eq!(player2_id, m.player_b);
    assert_eq!(status, "in_progress");
}

/// マッチ成立済みの対戦。`player_a`が先に待っていた側(`battle_matches.player1_id`)。
struct MatchedPlayers {
    match_id: String,
    player_a: String,
    player_b: String,
}

/// 2人のプレイヤーを作成してA→Bの順に待機列へ参加させ、成立した対戦を返す。
async fn create_match(app: Router) -> MatchedPlayers {
    let (token_a, player_a) = create_player(app.clone(), "secret-a", "プレイヤーA").await;
    let (token_b, player_b) = create_player(app.clone(), "secret-b", "プレイヤーB").await;

    assert_eq!(
        join_queue(app.clone(), &token_a).await.status(),
        StatusCode::OK
    );
    assert_eq!(
        join_queue(app.clone(), &token_b).await.status(),
        StatusCode::OK
    );

    let status = queue_status(app.clone(), &token_a).await;
    assert_eq!(status["status"], "matched");

    MatchedPlayers {
        match_id: status["matchId"].as_str().unwrap().to_string(),
        player_a,
        player_b,
    }
}

async fn report_result(app: Router, secret: Option<&str>, body: Value) -> axum::response::Response {
    let mut builder = Request::builder()
        .method("POST")
        .uri("/internal/battle/result")
        .header("Content-Type", "application/json");
    if let Some(secret) = secret {
        builder = builder.header("X-Internal-Secret", secret);
    }

    app.oneshot(builder.body(Body::from(body.to_string())).unwrap())
        .await
        .unwrap()
}

fn internal_secret() -> String {
    std::env::var("INTERNAL_API_SECRET").unwrap()
}

/// 結果報告のリクエストボディ(BattleServerの`BattleResultRequest`と同じ形)。
fn result_body(
    match_id: &str,
    winner_id: &str,
    player1: (&str, Value),
    player2: (&str, Value),
    turns: Vec<Value>,
) -> Value {
    json!({
        "matchId": match_id,
        "winnerId": winner_id,
        "player1Id": player1.0,
        "player2Id": player2.0,
        "player1SelectedPachimon": player1.1,
        "player2SelectedPachimon": player2.1,
        "turns": turns
    })
}

/// BattleServerの`BattleTurnRecord`と同じ形のターン1件分。
fn turn(turn_number: i32, player_id: &str, damage: i32) -> Value {
    json!({
        "turnNumber": turn_number,
        "playerId": player_id,
        "actionData": { "type": "Move", "moveId": "19", "partySlot": null },
        "resultData": {
            "hit": true, "critical": false, "effectiveness": "Normal",
            "damageDealt": damage, "targetRemainingHp": 115,
            "targetFainted": false, "newActiveIndex": null
        }
    })
}

async fn gems_of(pool: &MySqlPool, player_id: &str) -> i32 {
    sqlx::query_scalar("SELECT quantity FROM player_items WHERE player_id = ? AND item_id = 1")
        .bind(player_id)
        .fetch_one(pool)
        .await
        .unwrap()
}

async fn match_status(pool: &MySqlPool, match_id: &str) -> String {
    sqlx::query_scalar("SELECT status FROM battle_matches WHERE match_id = ?")
        .bind(match_id)
        .fetch_one(pool)
        .await
        .unwrap()
}

async fn turn_count(pool: &MySqlPool, match_id: &str) -> i64 {
    sqlx::query_scalar("SELECT COUNT(*) FROM battle_turns WHERE match_id = ?")
        .bind(match_id)
        .fetch_one(pool)
        .await
        .unwrap()
}

/// 終了済み`battle_matches`の`(status, winner_id, player1_selected_pachimon, player2_selected_pachimon)`。
async fn fetch_finished_match(pool: &MySqlPool, match_id: &str) -> (String, String, Value, Value) {
    let (status, winner_id, selected1, selected2): (
        String,
        String,
        sqlx::types::Json<Value>,
        sqlx::types::Json<Value>,
    ) = sqlx::query_as(
        "SELECT status, winner_id, player1_selected_pachimon, player2_selected_pachimon \
         FROM battle_matches WHERE match_id = ? AND ended_at IS NOT NULL",
    )
    .bind(match_id)
    .fetch_one(pool)
    .await
    .unwrap();

    (status, winner_id, selected1.0, selected2.0)
}

#[sqlx::test]
async fn test_report_result_success(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;
    let gems_a_before = gems_of(&pool, &m.player_a).await;
    let gems_b_before = gems_of(&pool, &m.player_b).await;

    let turns = vec![
        turn(1, &m.player_a, 25),
        turn(1, &m.player_b, 30),
        turn(2, &m.player_a, 40),
    ];
    let body = result_body(
        &m.match_id,
        &m.player_a,
        (&m.player_a, json!(["a1", "a2", "a3"])),
        (&m.player_b, json!(["b1", "b2", "b3"])),
        turns.clone(),
    );
    let response = report_result(app, Some(&internal_secret()), body).await;
    assert_eq!(response.status(), StatusCode::OK);

    let (status, winner_id, selected1, selected2) = fetch_finished_match(&pool, &m.match_id).await;
    assert_eq!(status, "finished");
    assert_eq!(winner_id, m.player_a);
    assert_eq!(selected1, json!(["a1", "a2", "a3"]));
    assert_eq!(selected2, json!(["b1", "b2", "b3"]));

    // ターン番号→与ダメージの順に並べ、送った順と突き合わせる
    let saved: Vec<(
        i32,
        String,
        sqlx::types::Json<Value>,
        sqlx::types::Json<Value>,
    )> = sqlx::query_as(
        "SELECT turn_number, player_id, action_data, result_data FROM battle_turns \
             WHERE match_id = ? ORDER BY turn_number, result_data->'$.damageDealt'",
    )
    .bind(&m.match_id)
    .fetch_all(&pool)
    .await
    .unwrap();
    assert_eq!(saved.len(), turns.len());
    for (row, expected) in saved.iter().zip(&turns) {
        assert_eq!(row.0, expected["turnNumber"]);
        assert_eq!(row.1, expected["playerId"].as_str().unwrap());
        assert_eq!(row.2.0, expected["actionData"]);
        assert_eq!(row.3.0, expected["resultData"]);
    }

    assert_eq!(
        gems_of(&pool, &m.player_a).await,
        gems_a_before + BATTLE_WIN_REWARD_GEMS
    );
    assert_eq!(gems_of(&pool, &m.player_b).await, gems_b_before);
}

#[sqlx::test]
async fn test_report_result_swapped_player_order(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;

    // BattleServerには後から待機列に入ったBが先に接続した(player1 = B)
    let body = result_body(
        &m.match_id,
        &m.player_b,
        (&m.player_b, json!(["b1", "b2", "b3"])),
        (&m.player_a, json!(["a1", "a2", "a3"])),
        vec![turn(1, &m.player_b, 25)],
    );
    let response = report_result(app, Some(&internal_secret()), body).await;
    assert_eq!(response.status(), StatusCode::OK);

    // battle_matches側はマッチ成立時の順番(player1 = A)のまま、選出がIDで突き合わされる
    let (_, winner_id, selected1, selected2) = fetch_finished_match(&pool, &m.match_id).await;
    assert_eq!(winner_id, m.player_b);
    assert_eq!(selected1, json!(["a1", "a2", "a3"]));
    assert_eq!(selected2, json!(["b1", "b2", "b3"]));
}

#[sqlx::test]
async fn test_report_result_opponent_never_joined(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;
    let gems_b_before = gems_of(&pool, &m.player_b).await;

    // Bだけが接続し、Aは一度も接続しなかった
    let body = result_body(
        &m.match_id,
        &m.player_b,
        (&m.player_b, json!([])),
        ("", json!([])),
        vec![],
    );
    let response = report_result(app, Some(&internal_secret()), body).await;
    assert_eq!(response.status(), StatusCode::OK);

    let (status, winner_id, selected1, selected2) = fetch_finished_match(&pool, &m.match_id).await;
    assert_eq!(status, "finished");
    assert_eq!(winner_id, m.player_b);
    assert_eq!(selected1, json!([]));
    assert_eq!(selected2, json!([]));
    assert_eq!(turn_count(&pool, &m.match_id).await, 0);
    assert_eq!(
        gems_of(&pool, &m.player_b).await,
        gems_b_before + BATTLE_WIN_REWARD_GEMS
    );
}

#[sqlx::test]
async fn test_report_result_wrong_secret(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;
    let body = result_body(
        &m.match_id,
        &m.player_a,
        (&m.player_a, json!([])),
        (&m.player_b, json!([])),
        vec![],
    );

    let response = report_result(app.clone(), Some("wrong-secret"), body.clone()).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
    let response = report_result(app, None, body).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);

    assert_eq!(match_status(&pool, &m.match_id).await, "in_progress");
}

#[sqlx::test]
async fn test_report_result_unknown_match(pool: MySqlPool) {
    let app = setup_app(pool).await;
    let m = create_match(app.clone()).await;

    let body = result_body(
        &ulid::Ulid::new().to_string(),
        &m.player_a,
        (&m.player_a, json!([])),
        (&m.player_b, json!([])),
        vec![],
    );
    let response = report_result(app, Some(&internal_secret()), body).await;
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}

#[sqlx::test]
async fn test_report_result_non_participant(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;
    let (_, outsider) = create_player(app.clone(), "secret-c", "プレイヤーC").await;

    let body_with = |winner_id: &str, turn_player_id: &str| {
        result_body(
            &m.match_id,
            winner_id,
            (&m.player_a, json!([])),
            (&m.player_b, json!([])),
            vec![turn(1, turn_player_id, 10)],
        )
    };

    // 勝者が参加者でない
    let response = report_result(
        app.clone(),
        Some(&internal_secret()),
        body_with(&outsider, &m.player_a),
    )
    .await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);

    // ターンの行動者が参加者でない
    let response = report_result(
        app,
        Some(&internal_secret()),
        body_with(&m.player_a, &outsider),
    )
    .await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);

    assert_eq!(match_status(&pool, &m.match_id).await, "in_progress");
}

#[sqlx::test]
async fn test_report_result_twice_conflict(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;
    let gems_a_before = gems_of(&pool, &m.player_a).await;
    let body = result_body(
        &m.match_id,
        &m.player_a,
        (&m.player_a, json!(["a1"])),
        (&m.player_b, json!(["b1"])),
        vec![turn(1, &m.player_a, 25)],
    );

    let response = report_result(app.clone(), Some(&internal_secret()), body.clone()).await;
    assert_eq!(response.status(), StatusCode::OK);
    let response = report_result(app, Some(&internal_secret()), body).await;
    assert_eq!(response.status(), StatusCode::CONFLICT);

    // 2回目は何も変更しない(gemsの二重付与・ターンの二重記録が無い)
    assert_eq!(
        gems_of(&pool, &m.player_a).await,
        gems_a_before + BATTLE_WIN_REWARD_GEMS
    );
    assert_eq!(turn_count(&pool, &m.match_id).await, 1);
}

/// `battle_matches`の`(status, winner_id)`。`winner_id`はNULLの場合`None`。
async fn match_status_and_winner(pool: &MySqlPool, match_id: &str) -> (String, Option<String>) {
    sqlx::query_as("SELECT status, winner_id FROM battle_matches WHERE match_id = ?")
        .bind(match_id)
        .fetch_one(pool)
        .await
        .unwrap()
}

/// `started_at`を`hours`時間前にずらし、結果報告が届かないまま時間が経った対戦を再現する。
async fn age_match(pool: &MySqlPool, match_id: &str, hours: i32) {
    sqlx::query(
        "UPDATE battle_matches SET started_at = NOW(3) - INTERVAL ? HOUR WHERE match_id = ?",
    )
    .bind(hours)
    .bind(match_id)
    .execute(pool)
    .await
    .unwrap();
}

#[sqlx::test]
async fn test_report_result_no_winner(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;
    let gems_a_before = gems_of(&pool, &m.player_a).await;
    let gems_b_before = gems_of(&pool, &m.player_b).await;

    // 両者放置で勝者なし
    let body = result_body(
        &m.match_id,
        "",
        (&m.player_a, json!(["a1"])),
        (&m.player_b, json!(["b1"])),
        vec![turn(1, &m.player_a, 0), turn(1, &m.player_b, 0)],
    );
    let response = report_result(app, Some(&internal_secret()), body).await;
    assert_eq!(response.status(), StatusCode::OK);

    assert_eq!(
        match_status_and_winner(&pool, &m.match_id).await,
        ("aborted".to_string(), None)
    );
    assert_eq!(turn_count(&pool, &m.match_id).await, 2);
    assert_eq!(gems_of(&pool, &m.player_a).await, gems_a_before);
    assert_eq!(gems_of(&pool, &m.player_b).await, gems_b_before);
}

#[sqlx::test]
async fn test_report_result_after_no_winner_conflict(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;
    let gems_a_before = gems_of(&pool, &m.player_a).await;
    let body_with = |winner_id: &str| {
        result_body(
            &m.match_id,
            winner_id,
            (&m.player_a, json!([])),
            (&m.player_b, json!([])),
            vec![],
        )
    };

    let response = report_result(app.clone(), Some(&internal_secret()), body_with("")).await;
    assert_eq!(response.status(), StatusCode::OK);

    // 勝者なしで報告済みの対戦には、勝者ありでも勝者なしでも再報告できない
    for winner_id in ["", m.player_a.as_str()] {
        let response =
            report_result(app.clone(), Some(&internal_secret()), body_with(winner_id)).await;
        assert_eq!(response.status(), StatusCode::CONFLICT);
    }
    assert_eq!(gems_of(&pool, &m.player_a).await, gems_a_before);
}

#[sqlx::test]
async fn test_abort_stale_matches(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;

    // 同じ2人で、成立したばかりの対戦をもう1つ用意する
    let fresh_match_id = ulid::Ulid::new().to_string();
    sqlx::query(
        "INSERT INTO battle_matches (match_id, player1_id, player2_id, status, started_at) \
         VALUES (?, ?, ?, 'in_progress', NOW(3))",
    )
    .bind(&fresh_match_id)
    .bind(&m.player_a)
    .bind(&m.player_b)
    .execute(&pool)
    .await
    .unwrap();

    age_match(&pool, &m.match_id, 2).await;

    let aborted = abort_stale_matches(&pool, STALE_MATCH_TIMEOUT)
        .await
        .ok()
        .unwrap();
    assert_eq!(aborted, 1);
    assert_eq!(
        match_status_and_winner(&pool, &m.match_id).await,
        ("aborted".to_string(), None)
    );
    assert_eq!(match_status(&pool, &fresh_match_id).await, "in_progress");

    // 打ち切り済みの対戦は再度対象にならない
    assert_eq!(
        abort_stale_matches(&pool, STALE_MATCH_TIMEOUT)
            .await
            .ok()
            .unwrap(),
        0
    );
}

#[sqlx::test]
async fn test_report_result_after_stale_abort(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let m = create_match(app.clone()).await;
    let gems_b_before = gems_of(&pool, &m.player_b).await;

    age_match(&pool, &m.match_id, 2).await;
    abort_stale_matches(&pool, STALE_MATCH_TIMEOUT)
        .await
        .ok()
        .unwrap();
    assert_eq!(match_status(&pool, &m.match_id).await, "aborted");

    // 1時間を超える対戦の結果が、打ち切り後に届いた
    let body = result_body(
        &m.match_id,
        &m.player_b,
        (&m.player_a, json!(["a1"])),
        (&m.player_b, json!(["b1"])),
        vec![turn(1, &m.player_b, 25)],
    );
    let response = report_result(app, Some(&internal_secret()), body).await;
    assert_eq!(response.status(), StatusCode::OK);

    let (status, winner_id, _, _) = fetch_finished_match(&pool, &m.match_id).await;
    assert_eq!(status, "finished");
    assert_eq!(winner_id, m.player_b);
    assert_eq!(
        gems_of(&pool, &m.player_b).await,
        gems_b_before + BATTLE_WIN_REWARD_GEMS
    );
}

async fn battle_loadouts(
    app: Router,
    secret: Option<&str>,
    body: Value,
) -> axum::response::Response {
    let mut builder = Request::builder()
        .method("POST")
        .uri("/internal/battle/loadouts")
        .header("Content-Type", "application/json");
    if let Some(secret) = secret {
        builder = builder.header("X-Internal-Secret", secret);
    }

    app.oneshot(builder.body(Body::from(body.to_string())).unwrap())
        .await
        .unwrap()
}

async fn owned_pachimon_ids(pool: &MySqlPool, player_id: &str) -> Vec<String> {
    sqlx::query_scalar("SELECT player_pachimon_id FROM player_pachimon WHERE player_id = ?")
        .bind(player_id)
        .fetch_all(pool)
        .await
        .unwrap()
}

#[sqlx::test]
async fn test_battle_loadouts_success(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let (_, player_id) = create_player(app.clone(), "loadout-secret", "選出太郎").await;
    let starter_id = owned_pachimon_ids(&pool, &player_id).await.remove(0);

    // 2体目を直接追加し、努力値・技の並びがそのまま返ることを確かめる。
    let second_id = "01J0000000000000000000LOAD".to_string();
    sqlx::query(
        "INSERT INTO player_pachimon (player_pachimon_id, player_id, pachimon_id, effort_values)          VALUES (?, ?, 1, ?)",
    )
    .bind(&second_id)
    .bind(&player_id)
    .bind(json!({"hp": 4, "atk": 8, "def": 0, "spatk": 0, "spdef": 0, "speed": 52}))
    .execute(&pool)
    .await
    .unwrap();
    sqlx::query(
        "INSERT INTO moves (move_id, name, move_type, category, base_power, accuracy, max_pp)          VALUES (2, 'でんこうせっか', 1, 1, 40, 100, 30)",
    )
    .execute(&pool)
    .await
    .unwrap();
    for (move_row_id, slot, move_id) in [
        ("01J0000000000000000000MOV2", 2, 1),
        ("01J0000000000000000000MOV1", 1, 2),
    ] {
        sqlx::query(
            "INSERT INTO player_pachimon_moves (player_pachimon_move_id, player_pachimon_id, slot, move_id)              VALUES (?, ?, ?, ?)",
        )
        .bind(move_row_id)
        .bind(&second_id)
        .bind(slot)
        .bind(move_id)
        .execute(&pool)
        .await
        .unwrap();
    }

    let response = battle_loadouts(
        app,
        Some(&internal_secret()),
        json!({ "playerId": player_id, "playerPachimonIds": [second_id, starter_id] }),
    )
    .await;
    assert_eq!(response.status(), StatusCode::OK);
    let json = json_body(response).await;

    assert_eq!(
        json,
        json!({
            "pachimon": [
                {
                    "playerPachimonId": second_id,
                    "pachimonId": 1,
                    "effortValues": {"hp": 4, "atk": 8, "def": 0, "spatk": 0, "spdef": 0, "speed": 52},
                    "moveIds": [2, 1],
                },
                {
                    "playerPachimonId": starter_id,
                    "pachimonId": 1,
                    "effortValues": {"hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 0},
                    "moveIds": [1],
                },
            ]
        })
    );
}

#[sqlx::test]
async fn test_battle_loadouts_not_owned(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let (_, player_a) = create_player(app.clone(), "loadout-a", "A").await;
    let (_, player_b) = create_player(app.clone(), "loadout-b", "B").await;
    let own_id = owned_pachimon_ids(&pool, &player_a).await.remove(0);
    let other_id = owned_pachimon_ids(&pool, &player_b).await.remove(0);

    for ids in [
        json!([own_id, other_id]),
        json!([own_id, "01J0000000000000000000NONE"]),
    ] {
        let response = battle_loadouts(
            app.clone(),
            Some(&internal_secret()),
            json!({ "playerId": player_a, "playerPachimonIds": ids }),
        )
        .await;
        assert_eq!(response.status(), StatusCode::NOT_FOUND);
    }
}

#[sqlx::test]
async fn test_battle_loadouts_wrong_secret(pool: MySqlPool) {
    let app = setup_app(pool.clone()).await;
    let (_, player_id) = create_player(app.clone(), "loadout-secret", "選出太郎").await;
    let ids = owned_pachimon_ids(&pool, &player_id).await;
    let body = json!({ "playerId": player_id, "playerPachimonIds": ids });

    let response = battle_loadouts(app.clone(), Some("wrong-secret"), body.clone()).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
    let response = battle_loadouts(app, None, body).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}
