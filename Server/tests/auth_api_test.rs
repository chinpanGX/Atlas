// tests/auth_api_test.rs
//
// POST /devices/authenticate (デバイス認証) と GET /auth/verify (トークン検証) のテスト。
// - test_authenticate_and_verify_flow: 認証→検証の正常系。再認証で新トークンが発行され、
//   旧トークンが無効化される(1device_id=1トークン)ことも確認
// - test_authenticate_wrong_secret_key: secret_key不一致で401になることを確認
// - test_authenticate_unknown_device: 存在しないdevice_idで401になることを確認
// - test_verify_without_token: Authorizationヘッダーが無い場合に401になることを確認
// - test_verify_invalid_token: 存在しないトークンで401になることを確認
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

async fn register_device(app: Router, secret_key: &str) -> String {
    let response = app
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

    assert_eq!(response.status(), StatusCode::OK);

    let body = axum::body::to_bytes(response.into_body(), usize::MAX)
        .await
        .unwrap();
    let json: Value = serde_json::from_slice(&body).unwrap();
    json["deviceId"].as_str().unwrap().to_string()
}

async fn authenticate(app: Router, device_id: &str, secret_key: &str) -> axum::response::Response {
    app.oneshot(
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
    .unwrap()
}

async fn verify(app: Router, access_token: Option<&str>) -> axum::response::Response {
    let mut builder = Request::builder().method("GET").uri("/auth/verify");
    if let Some(token) = access_token {
        builder = builder.header(header::AUTHORIZATION, format!("Bearer {token}"));
    }

    app.oneshot(builder.body(Body::empty()).unwrap())
        .await
        .unwrap()
}

/// デバイス登録→認証→トークン検証の一連の流れが成功し、
/// 再認証で古いトークンが無効化される(新トークンのみ有効)ことを確認する。
#[sqlx::test]
async fn test_authenticate_and_verify_flow(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let device_id = register_device(app.clone(), "test-secret-123").await;

    let auth_response = authenticate(app.clone(), &device_id, "test-secret-123").await;
    assert_eq!(auth_response.status(), StatusCode::OK);

    let body = axum::body::to_bytes(auth_response.into_body(), usize::MAX)
        .await
        .unwrap();
    let json: Value = serde_json::from_slice(&body).unwrap();
    let access_token = json["accessToken"].as_str().unwrap().to_string();
    assert!(json["expiresIn"].as_i64().unwrap() > 0);

    let verify_response = verify(app.clone(), Some(&access_token)).await;
    assert_eq!(verify_response.status(), StatusCode::OK);

    // 再認証すると新しいトークンが発行され、古いトークンは無効化される(1device_id=1トークン)
    let auth_response2 = authenticate(app.clone(), &device_id, "test-secret-123").await;
    assert_eq!(auth_response2.status(), StatusCode::OK);

    let body2 = axum::body::to_bytes(auth_response2.into_body(), usize::MAX)
        .await
        .unwrap();
    let json2: Value = serde_json::from_slice(&body2).unwrap();
    let new_access_token = json2["accessToken"].as_str().unwrap().to_string();
    assert_ne!(access_token, new_access_token);

    let old_token_response = verify(app.clone(), Some(&access_token)).await;
    assert_eq!(old_token_response.status(), StatusCode::UNAUTHORIZED);

    let new_token_response = verify(app.clone(), Some(&new_access_token)).await;
    assert_eq!(new_token_response.status(), StatusCode::OK);
}

/// 誤った`secret_key`で認証すると401が返ることを確認する。
#[sqlx::test]
async fn test_authenticate_wrong_secret_key(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let device_id = register_device(app.clone(), "correct-secret").await;

    let response = authenticate(app.clone(), &device_id, "wrong-secret").await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

/// 存在しない`device_id`で認証すると401が返ることを確認する。
#[sqlx::test]
async fn test_authenticate_unknown_device(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = authenticate(app.clone(), "unknown-device-id", "any-secret").await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

/// `Authorization`ヘッダーが無い状態で検証すると401が返ることを確認する。
#[sqlx::test]
async fn test_verify_without_token(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = verify(app.clone(), None).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}

/// 存在しない(発行されていない)トークンで検証すると401が返ることを確認する。
#[sqlx::test]
async fn test_verify_invalid_token(pool: MySqlPool) {
    let state = AppState::from_pool(pool).await;
    let app = create_router(state);

    let response = verify(app.clone(), Some("not-a-real-token")).await;
    assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
}
