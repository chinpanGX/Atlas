// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Deserializer, Serialize, Serializer};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Rarity {
    S = 1,
    A = 2,
    B = 3,
    C = 4,
}

impl Serialize for Rarity {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        serializer.serialize_i64(*self as i64)
    }
}

impl<'de> Deserialize<'de> for Rarity {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        let value = i64::deserialize(deserializer)?;
        match value {
            1 => Ok(Rarity::S),
            2 => Ok(Rarity::A),
            3 => Ok(Rarity::B),
            4 => Ok(Rarity::C),
            other => Err(serde::de::Error::custom(format!(
                "unknown Rarity id: {other}"
            ))),
        }
    }
}
