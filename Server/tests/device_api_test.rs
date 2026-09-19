// tests/device_api_test.rs
//
// POST /devices (デバイス新規登録) のテスト。
// - test_register_device_endpoint: 登録リクエストが200を返すことを確認
use axum::{
    body::Body,
    http::{Request, StatusCode},
};
use serde_json::json;
use sqlx::MySqlPool;
use tower::ServiceExt;

// AppStateやrouterを組み立てる関数は、Serverクレート側からpub importする想定
use Server::routes::create_router;
use Server::state::AppState;

/// デバイス登録が成功し、200が返ることを確認する。
#[sqlx::test]
async fn test_register_device_endpoint(pool: MySqlPool) {
    let state = AppState { pool };
    let app = create_router(state);

    let response = app
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/devices")
                .header("Content-Type", "application/json")
                .body(Body::from(
                    json!({ "secretKey": "test-secret-123" }).to_string(),
                ))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::OK);
}
