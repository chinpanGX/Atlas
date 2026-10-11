// 環境構築後のセットアップ確認専用スクリプト。
// tokio + MySQL(sqlx)が正しく動作し、.env の DATABASE_URL で DB につながるかどうかだけを確認する目的で用意している。
// 実際のAtlasサーバーの実装は src/main.rs 側で行う。
//
// 実行方法: cargo run --example setup_check
#[tokio::main]
async fn main() {
    dotenvy::dotenv().ok();
    let database_url = std::env::var("DATABASE_URL").expect("DATABASE_URL must be set in .env");

    let pool = sqlx::MySqlPool::connect(&database_url).await.unwrap();
    let one: i32 = sqlx::query_scalar("SELECT 1")
        .fetch_one(&pool)
        .await
        .unwrap();

    println!("Connected to MySQL (SELECT 1 = {one})");
}
