// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Serialize};

use super::rarity::Rarity;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScoutBannerRates {
    pub scout_banner_rate_id: i32,
    pub banner_id: String,
    pub rarity: Rarity,
    pub weight: i32,
}
