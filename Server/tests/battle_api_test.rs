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
