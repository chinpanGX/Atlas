// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Deserializer, Serialize, Serializer};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MoveCategory {
    Physical = 1,
    Special = 2,
    Status = 3,
}

impl Serialize for MoveCategory {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        serializer.serialize_i64(*self as i64)
    }
}

impl<'de> Deserialize<'de> for MoveCategory {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        let value = i64::deserialize(deserializer)?;
        match value {
            1 => Ok(MoveCategory::Physical),
            2 => Ok(MoveCategory::Special),
            3 => Ok(MoveCategory::Status),
            other => Err(serde::de::Error::custom(format!(
                "unknown MoveCategory id: {other}"
            ))),
        }
    }
}
