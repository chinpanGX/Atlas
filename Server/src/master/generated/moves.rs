// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Serialize};

use super::move_category::MoveCategory;
use super::pachimon_type::PachimonType;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Moves {
    pub move_id: i32,
    pub name: String,
    pub move_type: PachimonType,
    pub category: MoveCategory,
    pub base_power: i32,
    pub accuracy: i32,
    pub max_pp: i32,
}
