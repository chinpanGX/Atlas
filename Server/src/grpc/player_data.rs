//! 所持データの行と差分を、`.proto` の型にする。

use crate::model::{player_item, player_pachimon, player_party_slot};
use crate::proto::atlas::v1::{
    ItemsDiff, PachimonDiff, PachimonMoveMapDiff, PartySlot, PartySlotsDiff, PlayerDiff,
    PlayerItem, PlayerPachimon, PlayerPachimonMove,
};
use crate::service::player_diff::PlayerDiffCollector;

pub(crate) fn item_to_proto(item: player_item::PlayerItem) -> PlayerItem {
    PlayerItem {
        item_id: item.item_id,
        quantity: item.quantity,
    }
}

pub(crate) fn pachimon_to_proto(pachimon: player_pachimon::PlayerPachimon) -> PlayerPachimon {
    PlayerPachimon {
        player_pachimon_id: pachimon.player_pachimon_id,
        pachimon_id: pachimon.pachimon_id,
    }
}

pub(crate) fn move_to_proto(
    pachimon_move: player_pachimon::PlayerPachimonMove,
) -> PlayerPachimonMove {
    PlayerPachimonMove {
        player_pachimon_move_id: pachimon_move.player_pachimon_move_id,
        player_pachimon_id: pachimon_move.player_pachimon_id,
        slot: pachimon_move.slot,
        move_id: pachimon_move.move_id,
    }
}

pub(crate) fn party_slot_to_proto(slot: player_party_slot::PlayerPartySlot) -> PartySlot {
    PartySlot {
        party_slot_id: slot.party_slot_id,
        slot: slot.slot,
        player_pachimon_id: slot.player_pachimon_id,
    }
}

/// 集めた差分をレスポンスの`player_diff`にする。何も変わっていなければ`None`(フィールド無し)、
/// 変わっていない種別も`None`にする。
pub(crate) fn diff_to_proto(diff: &PlayerDiffCollector) -> Option<PlayerDiff> {
    if diff.is_empty() {
        return None;
    }

    Some(PlayerDiff {
        items: diff.has_item_changes().then(|| ItemsDiff {
            upserted: diff
                .items()
                .map(|item| PlayerItem {
                    item_id: item.item_id,
                    quantity: item.quantity,
                })
                .collect(),
            removed: Vec::new(),
        }),
        pachimon: diff.has_pachimon_changes().then(|| PachimonDiff {
            upserted: diff
                .pachimon()
                .map(|pachimon| PlayerPachimon {
                    player_pachimon_id: pachimon.player_pachimon_id.clone(),
                    pachimon_id: pachimon.pachimon_id,
                })
                .collect(),
            removed: Vec::new(),
        }),
        pachimon_move_map: diff
            .has_pachimon_move_changes()
            .then(|| PachimonMoveMapDiff {
                upserted: diff.pachimon_moves().cloned().map(move_to_proto).collect(),
                removed: Vec::new(),
            }),
        party_slots: diff.has_party_slot_changes().then(|| PartySlotsDiff {
            upserted: diff
                .party_slots()
                .into_iter()
                .map(|slot| PartySlot {
                    party_slot_id: slot.party_slot_id.clone(),
                    slot: slot.slot,
                    player_pachimon_id: slot.player_pachimon_id.clone(),
                })
                .collect(),
            removed: diff.removed_party_slots().cloned().collect(),
        }),
    })
}
