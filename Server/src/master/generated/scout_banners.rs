// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScoutBanners {
    pub banner_id: String,
    pub name: String,
    pub cost_per_roll: i32,
    pub start_at: String,
    pub end_at: String,
}
