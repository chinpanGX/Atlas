// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MoveGroupMoves {
    pub unique_id: i64,
    pub group_id: i64,
    pub move_id: i64,
    pub is_initial: bool,
}
