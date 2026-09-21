// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Deserializer, Serialize, Serializer};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TypeEffectiveness {
    Immune = 1,
    NotVeryEffective = 2,
    Normal = 3,
    SuperEffective = 4,
}

impl Serialize for TypeEffectiveness {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        serializer.serialize_i64(*self as i64)
    }
}

impl<'de> Deserialize<'de> for TypeEffectiveness {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        let value = i64::deserialize(deserializer)?;
        match value {
            1 => Ok(TypeEffectiveness::Immune),
            2 => Ok(TypeEffectiveness::NotVeryEffective),
            3 => Ok(TypeEffectiveness::Normal),
            4 => Ok(TypeEffectiveness::SuperEffective),
            other => Err(serde::de::Error::custom(format!(
                "unknown TypeEffectiveness id: {other}"
            ))),
        }
    }
}
