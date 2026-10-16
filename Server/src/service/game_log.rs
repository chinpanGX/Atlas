use voltech_grpc_error::Internal;
use voltech_grpc_transaction::{GameLog, TxContext};

use crate::model::player_item::PlayerItem;

/// ゲームログの種別: アイテムの増減。対象の ID はアイテムの ID、値は所持数。
pub const KIND_ITEM: &str = "item";

/// アイテムが増減した理由。ゲームログの `reason` に書く。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ItemReason {
    /// サインアップの初期の付与。
    SignUp,
    /// スカウトのロール。
    ScoutRoll,
    /// 対戦の勝利の報酬。
    BattleReward,
    /// 開発用の API。
    Debug,
}

impl ItemReason {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::SignUp => "sign_up",
            Self::ScoutRoll => "scout_roll",
            Self::BattleReward => "battle_reward",
            Self::Debug => "debug",
        }
    }
}

/// 1つのリクエストの間に書くゲームログを集める。
///
/// サービス層の関数は、基盤の `TxContext` ではなく普通のトランザクションを受け取るので、ここに記録しておき、
/// ハンドラーが `write_to` で `TxContext` に書く。書いたゲームログはコミットしたときだけ残る。
#[derive(Debug, Default)]
pub struct GameLogCollector {
    logs: Vec<GameLog>,
}

impl GameLogCollector {
    /// アイテムの増減を記録する。`delta` は取得なら正、消費なら負。`after` は更新後の所持アイテム。
    pub fn record_item(&mut self, after: &PlayerItem, delta: i32, reason: ItemReason) {
        self.logs.push(
            GameLog::new(
                KIND_ITEM,
                i64::from(delta),
                i64::from(after.quantity),
                reason.as_str(),
            )
            .target(after.item_id),
        );
    }

    /// 記録したゲームログを `ctx` に書く。
    ///
    /// # Errors
    /// `NO_SIDE_EFFECTS` の RPC か、`AuthLayer` を付けていないサービスで書いたときに `Internal` を返す。
    pub fn write_to(self, ctx: &mut TxContext) -> Result<(), Internal> {
        for log in self.logs {
            ctx.write_game_log(log)?;
        }
        Ok(())
    }

    /// 記録したゲームログ。
    pub fn logs(&self) -> &[GameLog] {
        &self.logs
    }
}
