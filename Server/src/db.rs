use sqlx::MySqlPool;
use sqlx::migrate::MigrateError;

/// gRPC 基盤のテーブル(デバイス・トークン・直前のレスポンス)と、Atlas のテーブルを作成・更新する。
/// 基盤のマイグレーションはクレートに埋め込まれていて sqlx-cli から流せないので、起動時にまとめて流す。
/// 履歴は基盤と Atlas で別のテーブルに入る。
pub async fn migrate(pool: &MySqlPool) -> Result<(), MigrateError> {
    voltech_grpc_auth::migrate(pool).await?;
    voltech_grpc_transaction::migrate(pool).await?;
    sqlx::migrate!("./migrations").run(pool).await
}
