// 環境構築後のセットアップ確認専用スクリプト。
// axum + tokio + MySQL(sqlx)が正しく動作するかどうかだけを確認する目的で用意している。
// 実際のAtlasサーバーの実装は src/main.rs 側で行う。
//
// 実行方法: cargo run --example setup_check
use axum::{Router, routing::get};

#[tokio::main]
async fn main() {
    let app = Router::new().route("/ping", get(ping_handler));

    let listener = tokio::net::TcpListener::bind("127.0.0.1:3000")
        .await
        .unwrap();

    println!("Server running on http://127.0.0.1:3000");
    axum::serve(listener, app).await.unwrap();
}

async fn ping_handler() -> &'static str {
    "pong"
}
