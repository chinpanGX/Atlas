// DO NOT EDIT: このファイルは master-data-pipeline によって自動生成されています。
// 直接編集せず、スプレッドシートまたは schema/ 配下の定義を変更してください。

use serde::{Deserialize, Deserializer, Serialize, Serializer};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PachimonType {
    None = 0,
    Normal = 1,
    Fire = 2,
    Water = 3,
    Electric = 4,
    Grass = 5,
    Ice = 6,
    Fighting = 7,
    Poison = 8,
    Ground = 9,
    Flying = 10,
    Psychic = 11,
    Bug = 12,
    Rock = 13,
    Ghost = 14,
    Dragon = 15,
    Dark = 16,
    Steel = 17,
    Fairy = 18,
}

impl Serialize for PachimonType {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        serializer.serialize_i32(*self as i32)
    }
}

impl<'de> Deserialize<'de> for PachimonType {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        let value = i32::deserialize(deserializer)?;
        match value {
            0 => Ok(PachimonType::None),
            1 => Ok(PachimonType::Normal),
            2 => Ok(PachimonType::Fire),
            3 => Ok(PachimonType::Water),
            4 => Ok(PachimonType::Electric),
            5 => Ok(PachimonType::Grass),
            6 => Ok(PachimonType::Ice),
            7 => Ok(PachimonType::Fighting),
            8 => Ok(PachimonType::Poison),
            9 => Ok(PachimonType::Ground),
            10 => Ok(PachimonType::Flying),
            11 => Ok(PachimonType::Psychic),
            12 => Ok(PachimonType::Bug),
            13 => Ok(PachimonType::Rock),
            14 => Ok(PachimonType::Ghost),
            15 => Ok(PachimonType::Dragon),
            16 => Ok(PachimonType::Dark),
            17 => Ok(PachimonType::Steel),
            18 => Ok(PachimonType::Fairy),
            other => Err(serde::de::Error::custom(format!(
                "unknown PachimonType id: {other}"
            ))),
        }
    }
}
