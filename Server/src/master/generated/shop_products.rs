// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ShopProducts {
    pub shop_product_id: i32,
    pub item_id: i32,
    pub amount: i32,
    pub cost_item_id: i32,
    pub cost_amount: i32,
}
