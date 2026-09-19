use sqlx::MySqlPool;

use super::{Pachimon, PachimonType, Rarity};

/// 起動時にDBから読み込むマスタデータのメモリキャッシュ。
///
/// APIサーバーはリクエストのたびにマスタテーブルへ問い合わせず、起動時に一度だけ
/// `load`でこのキャッシュを構築し、以降は`AppState`経由で参照するだけにする
/// (`docs/notes/api-design.md`の「マスターデータ管理」参照)。マスタ更新の反映は
/// `seed_master_data`コマンドでのDB再投入とサーバー再起動で行う。
pub struct MasterData {
    pub pachimon: Vec<Pachimon>,
}

impl MasterData {
    /// MySQLの各マスタテーブルから全件読み込む。
    ///
    /// # Errors
    /// DBアクセスに失敗した場合に`sqlx::Error`を返す。
    pub async fn load(pool: &MySqlPool) -> Result<Self, sqlx::Error> {
        let pachimon = load_pachimon(pool).await?;

        Ok(MasterData { pachimon })
    }
}

#[derive(sqlx::FromRow)]
struct PachimonRow {
    pachimon_id: i64,
    name: String,
    primary_type: u8,
    secondary_type: u8,
    base_hp: i64,
    base_atk: i64,
    base_def: i64,
    base_spatk: i64,
    base_spdef: i64,
    base_speed: i64,
    rarity: u8,
    move_group_id: i64,
}

async fn load_pachimon(pool: &MySqlPool) -> Result<Vec<Pachimon>, sqlx::Error> {
    let rows: Vec<PachimonRow> = sqlx::query_as(
        "SELECT pachimon_id, name, primary_type, secondary_type, base_hp, base_atk, base_def, \
         base_spatk, base_spdef, base_speed, rarity, move_group_id \
         FROM pachimon ORDER BY pachimon_id",
    )
    .fetch_all(pool)
    .await?;

    Ok(rows.into_iter().map(pachimon_from_row).collect())
}

fn pachimon_from_row(row: PachimonRow) -> Pachimon {
    Pachimon {
        pachimon_id: row.pachimon_id,
        name: row.name,
        primary_type: pachimon_type_from_u8(row.primary_type),
        secondary_type: pachimon_type_from_u8(row.secondary_type),
        base_hp: row.base_hp,
        base_atk: row.base_atk,
        base_def: row.base_def,
        base_spatk: row.base_spatk,
        base_spdef: row.base_spdef,
        base_speed: row.base_speed,
        rarity: rarity_from_u8(row.rarity),
        move_group_id: row.move_group_id,
    }
}

/// DBのTINYINT UNSIGNED値を`PachimonType`へ変換する。
///
/// `PachimonType`の`Deserialize`実装(生成コード)をそのまま再利用することで、
/// 数値↔Enumの対応表を重複定義しない。
fn pachimon_type_from_u8(value: u8) -> PachimonType {
    serde_json::from_value(serde_json::Value::from(value))
        .unwrap_or_else(|_| panic!("不正なPachimonType値がDBに保存されています: {value}"))
}

/// DBのTINYINT UNSIGNED値を`Rarity`へ変換する(`pachimon_type_from_u8`と同様の理由で
/// `Deserialize`実装を再利用する)。
fn rarity_from_u8(value: u8) -> Rarity {
    serde_json::from_value(serde_json::Value::from(value))
        .unwrap_or_else(|_| panic!("不正なRarity値がDBに保存されています: {value}"))
}
