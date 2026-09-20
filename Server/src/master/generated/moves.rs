// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Serialize};

use super::move_category::MoveCategory;
use super::pachimon_type::PachimonType;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Moves {
    pub move_id: i64,
    pub name: String,
    pub move_type: PachimonType,
    pub category: MoveCategory,
    pub base_power: i64,
    pub accuracy: i64,
    pub max_pp: i64,
}
