use serde_json::json;
use sqlx::{MySql, Transaction};
use ulid::Ulid;
use voltech_grpc_error::Internal;
use voltech_grpc_transaction::{GameLog, GameLogSink, GameLogs, TxContext};

use crate::model::player_item::PlayerItem;
use crate::model::player_pachimon::{PlayerPachimon, PlayerPachimonMove, StatPoints};
use crate::model::player_party_slot::PlayerPartySlot;

/// ゲームログの種別: アイテムの増減。対象の ID はアイテムの ID、値は所持数。
pub const KIND_ITEM: &str = "item";
/// ゲームログの種別: パチモンの入手。対象の ID は個体の ID。
pub const KIND_PACHIMON: &str = "pachimon";
/// ゲームログの種別: パーティの変更。変更後の全ての枠を持つ。
pub const KIND_PARTY: &str = "party";
/// ゲームログの種別: 技の付け替え。対象の ID は個体の ID。
pub const KIND_PACHIMON_MOVE: &str = "pachimon_move";
/// ゲームログの種別: 対戦の結果。対象の ID は対戦の ID。
pub const KIND_BATTLE: &str = "battle";
/// ゲームログの種別: ステータスポイントの割り振りの変更。対象の ID は個体の ID。
pub const KIND_PACHIMON_STAT_POINTS: &str = "pachimon_stat_points";

/// 技の付け替えの理由。付け替えの手段は今は1つだけ。
const REASON_SET_PACHIMON_MOVE: &str = "set_pachimon_move";
/// ステータスポイントの割り振りの理由。割り振りの手段は今は1つだけ。
const REASON_SET_PACHIMON_STAT_POINTS: &str = "set_pachimon_stat_points";
/// 対戦の結果の理由。結果はバトルサーバーの報告でだけ決まる。
const REASON_BATTLE_RESULT: &str = "battle_result";

/// アイテムが増減した理由。ゲームログの `reason` に書く。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ItemReason {
    /// サインアップの初期の付与。
    SignUp,
    /// スカウトのロール。
    ScoutRoll,
    /// ショップの購入(払ったジェムと、受け取ったアイテムの両方)。
    ShopPurchase,
    /// 技の付け替えで使った育成チケット。
    SetPachimonMove,
    /// ステータスポイントの割り振りで使った育成チケット。
    SetPachimonStatPoints,
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
            Self::ShopPurchase => "shop_purchase",
            Self::SetPachimonMove => REASON_SET_PACHIMON_MOVE,
            Self::SetPachimonStatPoints => REASON_SET_PACHIMON_STAT_POINTS,
            Self::BattleReward => "battle_reward",
            Self::Debug => "debug",
        }
    }
}

/// パチモンを入手した理由。ゲームログの `reason` に書く。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PachimonReason {
    /// サインアップの初期パーティの付与。
    SignUp,
    /// スカウトの候補の選択。
    ScoutSelect,
    /// 開発用の API(ランダム編成)。
    Debug,
}

impl PachimonReason {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::SignUp => "sign_up",
            Self::ScoutSelect => "scout_select",
            Self::Debug => "debug",
        }
    }
}

/// パーティが変わった理由。ゲームログの `reason` に書く。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PartyReason {
    /// サインアップの初期パーティの付与。
    SignUp,
    /// プレイヤーの編成。
    SetParty,
    /// 開発用の API(ランダム編成)。
    Debug,
}

impl PartyReason {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::SignUp => "sign_up",
            Self::SetParty => "set_party",
            Self::Debug => "debug",
        }
    }
}

/// 対戦の結果(そのプレイヤーから見たもの)。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BattleOutcome {
    Win,
    Lose,
    /// 勝者なしで終わった(両者とも未選出・両者とも放置など)。
    NoWinner,
}

impl BattleOutcome {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Win => "win",
            Self::Lose => "lose",
            Self::NoWinner => "no_winner",
        }
    }
}

/// 1つのリクエストの間に書くゲームログを、所持データの持ち主ごとに集める。
///
/// サービス層の関数は、基盤の `TxContext` ではなく普通のトランザクションを受け取るので、ここに記録しておき、
/// クライアント向けのハンドラーは `write_to` で `TxContext` に、内部 API は `commit` で書く。
/// 書いたゲームログはコミットしたときだけ残る。
#[derive(Debug, Default)]
pub struct GameLogCollector {
    logs: Vec<(String, GameLog)>,
}

impl GameLogCollector {
    /// アイテムの増減を記録する。`delta` は取得なら正、消費なら負。`after` は更新後の所持アイテム。
    pub fn record_item(&mut self, after: &PlayerItem, delta: i32, reason: ItemReason) {
        let log = GameLog::new(
            KIND_ITEM,
            i64::from(delta),
            i64::from(after.quantity),
            reason.as_str(),
        )
        .target(after.item_id);
        self.logs.push((after.player_id.clone(), log));
    }

    /// パチモンの入手を、個体の ID・種族の ID・入手した時点の技で記録する。
    pub fn record_pachimon(
        &mut self,
        pachimon: &PlayerPachimon,
        moves: &[PlayerPachimonMove],
        reason: PachimonReason,
    ) {
        let move_ids: Vec<i32> = moves.iter().map(|m| m.move_id).collect();
        let log = GameLog::event(KIND_PACHIMON, reason.as_str())
            .target(&pachimon.player_pachimon_id)
            .field("pachimon_id", pachimon.pachimon_id)
            .field("move_ids", move_ids);
        self.logs.push((pachimon.player_id.clone(), log));
    }

    /// パーティの変更を、変更後の全ての枠(枠の番号と個体の ID)で記録する。
    pub fn record_party(
        &mut self,
        player_id: &str,
        slots: &[PlayerPartySlot],
        reason: PartyReason,
    ) {
        let slots: Vec<_> = slots
            .iter()
            .map(|s| json!({"slot": s.slot, "player_pachimon_id": s.player_pachimon_id}))
            .collect();
        let log = GameLog::event(KIND_PARTY, reason.as_str()).field("slots", slots);
        self.logs.push((player_id.to_owned(), log));
    }

    /// 技の付け替えを、個体の ID・枠・前後の技の ID で記録する。空いていた枠に覚えたときは、前の技を `null` にする。
    pub fn record_pachimon_move(
        &mut self,
        player_id: &str,
        after: &PlayerPachimonMove,
        before_move_id: Option<i32>,
    ) {
        let log = GameLog::event(KIND_PACHIMON_MOVE, REASON_SET_PACHIMON_MOVE)
            .target(&after.player_pachimon_id)
            .field("slot", after.slot)
            .field("before_move_id", before_move_id)
            .field("after_move_id", after.move_id);
        self.logs.push((player_id.to_owned(), log));
    }

    /// ステータスポイントの割り振りの変更を、個体の ID・前後の割り振りで記録する。使った育成チケットは `record_item` で別に残る。
    pub fn record_stat_points(
        &mut self,
        player_id: &str,
        player_pachimon_id: &str,
        before: StatPoints,
        after: StatPoints,
    ) {
        let log = GameLog::event(KIND_PACHIMON_STAT_POINTS, REASON_SET_PACHIMON_STAT_POINTS)
            .target(player_pachimon_id)
            .field("before_stat_points", json!(before))
            .field("after_stat_points", json!(after));
        self.logs.push((player_id.to_owned(), log));
    }

    /// `player_id` から見た対戦の結果を、対戦の ID・相手・結果で記録する。
    pub fn record_battle(
        &mut self,
        player_id: &str,
        match_id: &str,
        opponent_id: &str,
        outcome: BattleOutcome,
    ) {
        let log = GameLog::event(KIND_BATTLE, REASON_BATTLE_RESULT)
            .target(match_id)
            .field("opponent_id", opponent_id)
            .field("result", outcome.as_str());
        self.logs.push((player_id.to_owned(), log));
    }

    /// 記録したゲームログを `ctx` に書く。持ち主は `ctx` の認証したプレイヤーになるので、
    /// 認証したプレイヤー自身の所持データだけを変える RPC で使う。
    ///
    /// # Errors
    /// `NO_SIDE_EFFECTS` の RPC か、`AuthLayer` を付けていないサービスで書いたときに `Internal` を返す。
    pub fn write_to(self, ctx: &mut TxContext) -> Result<(), Internal> {
        for (_, log) in self.logs {
            ctx.write_game_log(log)?;
        }
        Ok(())
    }

    /// 記録したゲームログを `sink` へ書き、`tx` をコミットする。基盤の `Transactions::run` を通らない内部 API で使う。
    ///
    /// `method` は RPC の名前(`game_logs.method`)、`request_id` は `x-request-id`(無ければ空文字)。
    ///
    /// # Errors
    /// 持ち主の `player_id` が ULID でないとき・コミットに失敗したときに `Internal` を返す。
    pub async fn commit(
        self,
        sink: GameLogSink,
        tx: Transaction<'_, MySql>,
        method: &str,
        request_id: &str,
    ) -> Result<(), Internal> {
        let mut logs = GameLogs::new(method).with_request_id(request_id);
        for (player_id, log) in self.logs {
            logs.write(Ulid::from_string(&player_id)?, log)?;
        }
        sink.commit(tx, logs).await
    }

    /// 記録したゲームログ。
    pub fn logs(&self) -> impl Iterator<Item = &GameLog> {
        self.logs.iter().map(|(_, log)| log)
    }
}
