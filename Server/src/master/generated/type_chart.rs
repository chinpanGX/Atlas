// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Serialize};

use super::pachimon_type::PachimonType;
use super::type_effectiveness::TypeEffectiveness;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TypeChart {
    pub type_chart_id: i64,
    pub attack_type: PachimonType,
    pub defend_type: PachimonType,
    pub effectiveness: TypeEffectiveness,
}
