// スカウトバナー投入コマンド。
//
// scout_bannersはmaster-data-pipelineの対象外(Shared/docs/design/scout.md参照)のため、
// このコマンドから直接DBへINSERT/UPSERTする。期間限定バナーの運用ツールは作らず、
// end_atを十分先の未来日付にした「常設バナー」を最低1件投入する
// (常設バナーが常に1件は開催中である状態を保証し、レギュラースカウトが常に実行できるようにする)。
//
// 実行方法: cargo run --bin seed_scout_banners
use chrono::{Duration, Utc};
use sqlx::MySqlPool;
use sqlx::types::Json;

/// 常設バナーの固定ID。再実行時も同じ行をUPSERTするため、生成のたびに変わるULIDではなく
/// 固定値を使う。
const REGULAR_BANNER_ID: &str = "SCOUT000000000000000000001";

#[tokio::main]
async fn main() {
    dotenvy::dotenv().ok();

    let database_url = std::env::var("DATABASE_URL").expect("DATABASE_URL must be set in .env");
    let pool = MySqlPool::connect(&database_url)
        .await
        .expect("Failed to connect to database");

    let rate_table = serde_json::json!({
        "S": 0.03,
        "A": 0.12,
        "B": 0.35,
        "C": 0.50,
    });

    let now = Utc::now().naive_utc();
    let end_at = now + Duration::days(365 * 100);

    sqlx::query(
        "INSERT INTO scout_banners (banner_id, name, rate_table, cost_per_roll, start_at, end_at) \
         VALUES (?, ?, ?, ?, ?, ?) \
         ON DUPLICATE KEY UPDATE \
            name = VALUES(name), \
            rate_table = VALUES(rate_table), \
            cost_per_roll = VALUES(cost_per_roll), \
            end_at = VALUES(end_at)",
    )
    .bind(REGULAR_BANNER_ID)
    .bind("レギュラースカウト")
    .bind(Json(&rate_table))
    .bind(150)
    .bind(now)
    .bind(end_at)
    .execute(&pool)
    .await
    .unwrap_or_else(|err| panic!("常設バナーのシード投入に失敗しました: {err}"));

    println!("スカウトバナーの投入が完了しました(banner_id: {REGULAR_BANNER_ID})");
}
