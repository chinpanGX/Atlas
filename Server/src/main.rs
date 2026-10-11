use std::time::Duration;

use Server::service::battle_service;
use Server::state::AppState;
use Server::{db, grpc};
use tracing_subscriber::EnvFilter;
use voltech_grpc_status::{FileStatusSource, ServerStatus};

/// クライアント向けの gRPC サーバーの待ち受け先(環境変数 `GRPC_ADDR` で上書きできる)。
const DEFAULT_GRPC_ADDR: &str = "127.0.0.1:50051";
/// BattleServer 向けの内部API(gRPC)の待ち受け先(環境変数 `SERVER_ADDR` で上書きできる)。
const DEFAULT_SERVER_ADDR: &str = "127.0.0.1:3000";
/// サーバーの状態の設定ファイル(環境変数 `SERVER_STATUS_PATH` で上書きできる。`Server/` からのパス)。
const DEFAULT_SERVER_STATUS_PATH: &str = "config/server_status.toml";
/// サーバーの状態の設定を読み直す間隔。再起動せずにメンテナンスを切り替えられる
const STATUS_RELOAD_INTERVAL: Duration = Duration::from_secs(5);

/// `RUST_LOG`未指定時のログ出力レベル。
/// 通常はリクエストごとの method/uri/ステータス/処理時間のみ出す。
/// ボディ・実行SQLまで見たいときは`RUST_LOG=debug`等で上書きする(server-dev-envスキル参照)
const DEFAULT_LOG_FILTER: &str = "info";

#[tokio::main]
async fn main() -> Result<(), grpc::BoxError> {
    dotenvy::dotenv().ok();

    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new(DEFAULT_LOG_FILTER)),
        )
        .init();

    let database_url = std::env::var("DATABASE_URL").expect("DATABASE_URL must be set in .env");
    let grpc_addr = std::env::var("GRPC_ADDR").unwrap_or_else(|_| DEFAULT_GRPC_ADDR.to_string());
    let server_addr =
        std::env::var("SERVER_ADDR").unwrap_or_else(|_| DEFAULT_SERVER_ADDR.to_string());
    let status_path = std::env::var("SERVER_STATUS_PATH")
        .unwrap_or_else(|_| DEFAULT_SERVER_STATUS_PATH.to_string());

    // 一度も読めなければ起動しない
    let status =
        ServerStatus::watch(FileStatusSource::new(&status_path), STATUS_RELOAD_INTERVAL).await?;

    let pool = sqlx::MySqlPool::connect(&database_url).await?;
    db::migrate(&pool).await?;
    let state = AppState::from_pool(pool).await;
    // 結果報告が届かないまま残った対戦の後始末(battle_service::abort_stale_matches参照)
    battle_service::spawn_stale_match_cleanup(state.pool.clone());

    let grpc_listener = tokio::net::TcpListener::bind(&grpc_addr).await?;
    let internal_listener = tokio::net::TcpListener::bind(&server_addr).await?;
    tracing::info!("gRPC server running on http://{}", grpc_addr);
    tracing::info!("Internal API running on http://{}", server_addr);

    // クライアント向けと内部APIは、マッチングの待機列(メモリ)を共有するため同じプロセスで動かす
    let internal = grpc::serve_internal(internal_listener, state.clone(), shutdown_signal());
    let dev = grpc::DevFeatures {
        // 開発中だけ gRPC リフレクションを有効にし、grpcurl や Postman から .proto 無しで呼べるようにする
        reflection: cfg!(debug_assertions),
        debug_api: debug_api_enabled(),
    };
    if dev.debug_api {
        tracing::info!("DebugService is enabled");
    }
    let grpc = grpc::serve(grpc_listener, state, status, dev, shutdown_signal());
    tokio::try_join!(internal, grpc)?;
    Ok(())
}

/// 開発用の API(`DebugService`)を登録するか。環境変数 `ENABLE_DEBUG_API` が `true`・`1` なら登録し、
/// それ以外の値なら登録しない。未設定ならデバッグビルドだけ登録する(`make dev` でそのまま使えるように)。
fn debug_api_enabled() -> bool {
    match std::env::var("ENABLE_DEBUG_API") {
        Ok(value) => matches!(value.trim(), "true" | "1"),
        Err(_) => cfg!(debug_assertions),
    }
}

async fn shutdown_signal() {
    let _ = tokio::signal::ctrl_c().await;
}
