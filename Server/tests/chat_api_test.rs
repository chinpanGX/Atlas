// tests/chat_api_test.rs
//
// POST /chat/send (メッセージ送信) と GET /chat/poll (メッセージ受信/取得) のテスト。
// - test_send_and_poll_messages: 送信→受信の正常系(件数・順序・送信者playerIdを確認)
// - test_send_message_unauthenticated: 未認証での送信が401になることを確認
// - test_send_message_without_player: プレイヤー未作成状態での送信が404になることを確認
// - test_poll_messages_unauthenticated: 未認証での受信が401になることを確認
// - test_poll_messages_empty: メッセージが1件も無い場合に空配列が返ることを確認
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

async fn create_player(app: Router, access_token: &str, nickname: &str) -> Value {
    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/players")
                .header("Content-Type", "application/json")
                .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
                .body(Body::from(json!({ "nickname": nickname }).to_string()))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(response.into_body(), usize::MAX)
        .await
        .unwrap();
    serde_json::from_slice(&body).unwrap()
}

async fn send_message(app: Router, access_token: &str, content: &str) -> axum::response::Response {
    app.oneshot(
        Request::builder()
            .method("POST")
            .uri("/chat/send")
            .header("Content-Type", "application/json")
            .header(header::AUTHORIZATION, format!("Bearer {access_token}"))
            .body(Body::from(json!({ "content": content }).to_string()))
            .unwrap(),
    )
    .await
    .unwrap()
}

async fn poll_messages(app: Router, access_token: Option<&str>) -> axum::response::Response {
    let mut builder = Request::builder().method("GET").uri("/chat/poll");
    if let Some(token) = access_token {
        builder = builder.header(header::AUTHORIZATION, format!("Bearer {token}"));
    }

    app.oneshot(builder.body(Body::empty()).unwrap())
        .await
        .unwrap()
}

/// メッセージを2件送信し、`GET /chat/poll`で送信順(古い順)・送信者の`playerId`が
/// 正しく返ることを確認する。
#[sqlx::test]
async fn test_send_and_poll_messages(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;
    let player = create_player(app.clone(), &access_token, "チャットテスト").await;
    let player_id = player["playerId"].as_str().unwrap().to_string();

    let send_response = send_message(app.clone(), &access_token, "こんにちは").await;
    assert_eq!(send_response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(send_response.into_body(), usize::MAX)
        .await
        .unwrap();
    let json: Value = serde_json::from_slice(&body).unwrap();
    assert!(!json["messageId"].as_str().unwrap().is_empty());

    let send_response2 = send_message(app.clone(), &access_token, "2件目").await;
    assert_eq!(send_response2.status(), StatusCode::OK);

    let poll_response = poll_messages(app.clone(), Some(&access_token)).await;
    assert_eq!(poll_response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(poll_response.into_body(), usize::MAX)
        .await
        .unwrap();
    let json: Value = serde_json::from_slice(&body).unwrap();
    let messages = json["messages"].as_array().unwrap();
    assert_eq!(messages.len(), 2);
    assert_eq!(messages[0]["content"], "こんにちは");
    assert_eq!(messages[1]["content"], "2件目");
    assert_eq!(messages[0]["playerId"], player_id);
}

/// `Authorization`ヘッダーが無い状態でメッセージ送信を行うと401が返ることを確認する。
#[sqlx::test]
async fn test_send_message_unauthenticated(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/chat/send")
                .header("Content-Type", "application/json")
                .body(Body::from(json!({ "content": "無認証" }).to_string()))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

/// デバイス認証は済んでいるがプレイヤー未作成の状態でメッセージを送信すると
/// 404が返ることを確認する。
#[sqlx::test]
async fn test_send_message_without_player(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;

    let response = send_message(app.clone(), &access_token, "プレイヤー未作成").await;
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}

/// `Authorization`ヘッダーが無い状態でメッセージ受信を行うと401が返ることを確認する。
#[sqlx::test]
async fn test_poll_messages_unauthenticated(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = poll_messages(app.clone(), None).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

/// メッセージが1件も送信されていない状態で受信すると、空の`messages`配列が
/// 返ることを確認する。
#[sqlx::test]
async fn test_poll_messages_empty(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let access_token = register_and_authenticate(app.clone(), "test-secret").await;

    let response = poll_messages(app.clone(), Some(&access_token)).await;
    assert_eq!(response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(response.into_body(), usize::MAX)
        .await
        .unwrap();
    let json: Value = serde_json::from_slice(&body).unwrap();
    assert_eq!(json["messages"].as_array().unwrap().len(), 0);
}
