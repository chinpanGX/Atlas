use std::sync::Arc;

use sqlx::MySqlPool;

use crate::master::MasterData;

#[derive(Clone)]
pub struct AppState {
    pub pool: MySqlPool,
    pub master: Arc<MasterData>,
}

impl AppState {
    pub async fn new(database_url: &str) -> Self {
        let pool = MySqlPool::connect(database_url)
            .await
            .expect("Failed to connect to database");

        Self::from_pool(pool).await
    }

    /// 既存のコネクションプールから`AppState`を組み立てる。
    ///
    /// 起動時にマスタデータをDBから読み込み、`Arc<MasterData>`としてメモリに保持する
    /// (リクエストのたびにマスタテーブルへ問い合わせないため)。
    pub async fn from_pool(pool: MySqlPool) -> Self {
        let master = MasterData::load(&pool)
            .await
            .expect("マスタデータの読み込みに失敗しました");

        AppState {
            pool,
            master: Arc::new(master),
        }
    }
}
