// tests/player_api_test.rs
//
// POST /players (プレイヤー作成) と GET /players/me (自分のプレイヤー情報取得) のテスト。
// - test_create_and_get_player: 作成→取得の正常系(nickname/gemsの内容を確認)
// - test_create_player_duplicate: 同一デバイスでの2件目の作成が409になることを確認
// - test_create_player_unauthenticated: 未認証での作成が401になることを確認
// - test_get_me_before_create: プレイヤー未作成状態での取得が404になることを確認
// - test_get_me_unauthenticated: 未認証での取得が401になることを確認
use axum::{
    Router,
    body::Body,
    http::{Request, StatusCode, header},
};
use serde_json::{Value, json};
use sqlx::MySqlPool;
use tower::ServiceExt;

use Server::routes::create_router;
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
    let body = axum::body::to_bytes(register_response.into_body(), usize::MAX)
        .await
        .unwrap();
    let device_id = serde_json::from_slice::<Value>(&body).unwrap()["deviceId"]
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
    let body = axum::body::to_bytes(auth_response.into_body(), usize::MAX)
        .await
        .unwrap();
    serde_json::from_slice::<Value>(&body).unwrap()["accessToken"]
        .as_str()
        .unwrap()
        .to_string()
}

async fn create_player(app: Router, access_token: &str, nickname: &str) -> axum::response::Response {
    app.oneshot(
        Request::builder()
            .method("POST")
            .uri("/players")
            .header("Content-Type", "application/json")
            .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
            .body(Body::from(json!({ "nickname": nickname }).to_string()))
            .unwrap(),
    )
    .await
    .unwrap()
}

async fn get_me(app: Router, access_token: Option<&str>) -> axum::response::Response {
    let mut builder = Request::builder().method("GET").uri("/players/me");
    if let Some(token) = access_token {
        builder = builder.header(header::AUTHORIZATION, format!("Bearer {token}"));
    }

    app.oneshot(builder.body(Body::empty()).unwrap())
        .await
        .unwrap()
}

/// プレイヤー作成が成功し、直後に`GET /players/me`で同じ内容が取得できることを確認する。
#[sqlx::test]
async fn test_create_and_get_player(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;

    let create_response = create_player(app.clone(), &access_token, "テストプレイヤー").await;
    assert_eq!(create_response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(create_response.into_body(), usize::MAX)
        .await
        .unwrap();
    let json: Value = serde_json::from_slice(&body).unwrap();
    assert_eq!(json["nickname"], "テストプレイヤー");
    assert_eq!(json["gems"], 0);
    assert!(!json["playerId"].as_str().unwrap().is_empty());

    let get_response = get_me(app.clone(), Some(&access_token)).await;
    assert_eq!(get_response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(get_response.into_body(), usize::MAX)
        .await
        .unwrap();
    let json: Value = serde_json::from_slice(&body).unwrap();
    assert_eq!(json["nickname"], "テストプレイヤー");
    assert_eq!(json["gems"], 0);
}

/// 同一デバイスで2回目のプレイヤー作成を行うと409(重複)が返ることを確認する。
#[sqlx::test]
async fn test_create_player_duplicate(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;

    let first = create_player(app.clone(), &access_token, "プレイヤー1").await;
    assert_eq!(first.status(), StatusCode::OK);

    let second = create_player(app.clone(), &access_token, "プレイヤー2").await;
    assert_eq!(second.status(), StatusCode::CONFLICT);
}

/// `Authorization`ヘッダーが無い状態でプレイヤー作成を行うと401が返ることを確認する。
#[sqlx::test]
async fn test_create_player_unauthenticated(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/players")
                .header("Content-Type", "application/json")
                .body(Body::from(json!({ "nickname": "無認証" }).to_string()))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

/// デバイス認証は済んでいるがプレイヤー未作成の状態で`GET /players/me`を呼ぶと
/// 404が返ることを確認する。
#[sqlx::test]
async fn test_get_me_before_create(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;

    let response = get_me(app.clone(), Some(&access_token)).await;
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}

/// `Authorization`ヘッダーが無い状態で`GET /players/me`を呼ぶと401が返ることを確認する。
#[sqlx::test]
async fn test_get_me_unauthenticated(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = get_me(app.clone(), None).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}
