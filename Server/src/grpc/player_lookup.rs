use sqlx::MySqlPool;
use voltech_grpc_auth::{BoxError, PlayerId, PlayerResolver, PlayerStatus};

use crate::service::player_service;

/// `players` テーブルからプレイヤーを引く、`PlayerResolver` の実装。
/// 基盤の `player_id`(ULID)を、Atlas のテーブルの `CHAR(26)` の文字列にして引く。
#[derive(Clone)]
pub struct PlayerLookup {
    pool: MySqlPool,
}

impl PlayerLookup {
    pub fn new(pool: MySqlPool) -> Self {
        Self { pool }
    }
}

impl PlayerResolver for PlayerLookup {
    async fn resolve(&self, player_id: PlayerId) -> Result<PlayerStatus, BoxError> {
        // Atlas にはアカウント停止が無いので Suspended は返さない
        let status = match player_service::find_by_id(&self.pool, &player_id.to_string())
            .await
            .map_err(|err| format!("failed to find player: {}", err.kind()))?
        {
            Some(_) => PlayerStatus::Active,
            None => PlayerStatus::NotCreated,
        };
        Ok(status)
    }
}
