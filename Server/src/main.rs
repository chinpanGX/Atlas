use Server::routes;
use Server::service::battle_service;
use Server::state::AppState;
use tracing_subscriber::EnvFilter;

const DEFAULT_SERVER_ADDR: &str = "127.0.0.1:3000";

/// `RUST_LOG`未指定時のログ出力レベル。
/// 通常はリクエストごとの method/uri/ステータス/処理時間のみ出す。
/// ボディ・実行SQLまで見たいときは`RUST_LOG=debug`等で上書きする(server-dev-envスキル参照)
const DEFAULT_LOG_FILTER: &str = "info";

#[tokio::main]
async fn main() {
    dotenvy::dotenv().ok();

    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new(DEFAULT_LOG_FILTER)),
        )
        .init();

    let database_url = std::env::var("DATABASE_URL").expect("DATABASE_URL must be set in .env");
    let server_addr =
        std::env::var("SERVER_ADDR").unwrap_or_else(|_| DEFAULT_SERVER_ADDR.to_string());

    let state = AppState::new(&database_url).await;
    // 結果報告が届かないまま残った対戦の後始末(battle_service::abort_stale_matches参照)
    battle_service::spawn_stale_match_cleanup(state.pool.clone());
    let app = routes::create_router(state);

    let listener = tokio::net::TcpListener::bind(&server_addr).await.unwrap();

    tracing::info!("Server running on http://{}", server_addr);
    axum::serve(listener, app).await.unwrap();
}
