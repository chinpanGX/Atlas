use std::sync::{Arc, Mutex};

use sqlx::MySqlPool;

use crate::master::MasterData;
use crate::service::matchmaking_service::{BattleConfig, MatchmakingQueue};

#[derive(Clone)]
pub struct AppState {
    pub pool: MySqlPool,
    pub master: Arc<MasterData>,
    /// マッチング待機列(プロセスメモリのみ、DB永続化しない)。ロック中にawaitしないため
    /// `std::sync::Mutex`を使う(`battle_matches`への書き込みはロックの外で行う)
    pub matchmaking: Arc<Mutex<MatchmakingQueue>>,
    /// BattleServerのURL・`battle_token`の署名シークレット・内部APIのサービス間シークレット
    pub battle: Arc<BattleConfig>,
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
    /// マッチング関連の設定(`BATTLE_TOKEN_SECRET`等)も環境変数から読み込む。
    pub async fn from_pool(pool: MySqlPool) -> Self {
        let master = MasterData::load(&pool)
            .await
            .expect("マスタデータの読み込みに失敗しました");

        AppState {
            pool,
            master: Arc::new(master),
            matchmaking: Arc::new(Mutex::new(MatchmakingQueue::default())),
            battle: Arc::new(BattleConfig::from_env()),
        }
    }
}
