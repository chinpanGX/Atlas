// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Serialize};

use super::pachimon_type::PachimonType;
use super::rarity::Rarity;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Pachimon {
    pub pachimon_id: i32,
    pub name: String,
    pub primary_type: PachimonType,
    pub secondary_type: PachimonType,
    pub base_hp: i32,
    pub base_atk: i32,
    pub base_def: i32,
    pub base_spatk: i32,
    pub base_spdef: i32,
    pub base_speed: i32,
    pub rarity: Rarity,
    pub move_group_id: i32,
}
