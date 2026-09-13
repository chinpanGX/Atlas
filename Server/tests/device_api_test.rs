// tests/device_api_test.rs
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
                    json!({ "secret_key": "test-secret-123" }).to_string(),
                ))
                .unwrap(),
        )
        .await
        .unwrap();

    assert_eq!(response.status(), StatusCode::OK);
}
