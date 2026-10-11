use std::collections::{BTreeMap, BTreeSet};

use crate::model::player_item::PlayerItem;
use crate::model::player_pachimon::{PlayerPachimon, PlayerPachimonMove};
use crate::model::player_party_slot::PlayerPartySlot;

/// 1つのリクエストの間に変わった所持データを集める(差分のコレクター)。
///
/// 書き込み関数が、DBに書くついでに変更後の値を記録する。同じ行が何回変わっても、最後の値だけを持つ。
/// ハンドラーが処理の最後にレスポンスの`player_diff`へ詰める。トランザクションがロールバックされたら
/// レスポンスごと捨てられるので、中身も残らない。
#[derive(Default)]
pub struct PlayerDiffCollector {
    items: BTreeMap<i32, PlayerItem>,
    pachimon: BTreeMap<String, PlayerPachimon>,
    pachimon_moves: BTreeMap<String, PlayerPachimonMove>,
    party_slots: BTreeMap<String, PlayerPartySlot>,
    removed_party_slots: BTreeSet<String>,
}

impl PlayerDiffCollector {
    /// 変更後の所持アイテムを記録する。
    pub fn record_item(&mut self, item: PlayerItem) {
        self.items.insert(item.item_id, item);
    }

    /// 追加・更新した所持個体を記録する。
    pub fn record_pachimon(&mut self, pachimon: PlayerPachimon) {
        self.pachimon
            .insert(pachimon.player_pachimon_id.clone(), pachimon);
    }

    /// 追加・更新した技の割当を記録する。
    pub fn record_pachimon_move(&mut self, pachimon_move: PlayerPachimonMove) {
        self.pachimon_moves
            .insert(pachimon_move.player_pachimon_move_id.clone(), pachimon_move);
    }

    /// 追加したパーティの枠の割当を記録する。
    pub fn record_party_slot(&mut self, slot: PlayerPartySlot) {
        self.removed_party_slots.remove(&slot.party_slot_id);
        self.party_slots.insert(slot.party_slot_id.clone(), slot);
    }

    /// 解除したパーティの枠の割当を記録する。
    pub fn record_removed_party_slot(&mut self, party_slot_id: String) {
        self.party_slots.remove(&party_slot_id);
        self.removed_party_slots.insert(party_slot_id);
    }

    pub fn items(&self) -> impl Iterator<Item = &PlayerItem> {
        self.items.values()
    }

    pub fn pachimon(&self) -> impl Iterator<Item = &PlayerPachimon> {
        self.pachimon.values()
    }

    pub fn pachimon_moves(&self) -> impl Iterator<Item = &PlayerPachimonMove> {
        self.pachimon_moves.values()
    }

    /// 追加したパーティの枠を、枠の番号の順に返す。
    pub fn party_slots(&self) -> Vec<&PlayerPartySlot> {
        let mut slots: Vec<_> = self.party_slots.values().collect();
        slots.sort_by_key(|slot| slot.slot);
        slots
    }

    pub fn removed_party_slots(&self) -> impl Iterator<Item = &String> {
        self.removed_party_slots.iter()
    }

    pub fn has_item_changes(&self) -> bool {
        !self.items.is_empty()
    }

    pub fn has_pachimon_changes(&self) -> bool {
        !self.pachimon.is_empty()
    }

    pub fn has_pachimon_move_changes(&self) -> bool {
        !self.pachimon_moves.is_empty()
    }

    pub fn has_party_slot_changes(&self) -> bool {
        !self.party_slots.is_empty() || !self.removed_party_slots.is_empty()
    }

    /// 何も変わっていないか。
    pub fn is_empty(&self) -> bool {
        !self.has_item_changes()
            && !self.has_pachimon_changes()
            && !self.has_pachimon_move_changes()
            && !self.has_party_slot_changes()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn party_slot(party_slot_id: &str, slot: i32) -> PlayerPartySlot {
        PlayerPartySlot {
            party_slot_id: party_slot_id.to_string(),
            player_id: "player".to_string(),
            slot,
            player_pachimon_id: format!("pachimon-{slot}"),
        }
    }

    /// 同じアイテムが何回変わっても、最後の所持数だけを持つ。
    #[test]
    fn keeps_last_item_value() {
        let mut diff = PlayerDiffCollector::default();
        assert!(diff.is_empty());

        for quantity in [100, 50] {
            diff.record_item(PlayerItem {
                player_id: "player".to_string(),
                item_id: 1,
                quantity,
            });
        }

        let items: Vec<_> = diff.items().map(|item| item.quantity).collect();
        assert_eq!(items, vec![50]);
        assert!(!diff.has_party_slot_changes());
    }

    /// 解除した後に同じ割当を追加し直したら追加だけが残り、追加は枠の番号の順に返る。
    #[test]
    fn party_slots_are_sorted_and_cancel_removal() {
        let mut diff = PlayerDiffCollector::default();
        diff.record_removed_party_slot("old".to_string());
        diff.record_removed_party_slot("b".to_string());
        diff.record_party_slot(party_slot("b", 2));
        diff.record_party_slot(party_slot("a", 1));

        let slots: Vec<_> = diff.party_slots().iter().map(|slot| slot.slot).collect();
        assert_eq!(slots, vec![1, 2]);
        let removed: Vec<_> = diff.removed_party_slots().cloned().collect();
        assert_eq!(removed, vec!["old".to_string()]);
    }
}
