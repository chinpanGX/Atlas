use sqlx::MySqlPool;

use super::{
    Items, MoveGroupMoves, Pachimon, PachimonType, Rarity, ScoutBanner, ScoutBannerRate,
    ShopProducts, StarterPartySlots,
};

/// 起動時にDBから読み込むマスタデータのメモリキャッシュ。
///
/// APIサーバーはリクエストのたびにマスタテーブルへ問い合わせず、起動時に一度だけ
/// `load`でこのキャッシュを構築し、以降は`AppState`経由で参照するだけにする。マスタ更新の反映は
/// `seed_master_data`コマンドでのDB再投入とサーバー再起動で行う。
///
/// `moves`/`move_groups`はスカウト等のロジックからは参照されない(技IDのみを扱うため)ので
/// キャッシュ対象外とし、DB上に存在すれば足りるものとしている。
pub struct MasterData {
    pub pachimon: Vec<Pachimon>,
    pub move_group_moves: Vec<MoveGroupMoves>,
    pub starter_party_slots: Vec<StarterPartySlots>,
    pub items: Vec<Items>,
    /// スカウトのバナーと排出率。開催期間外のものも含む(開催中かはリクエストのたびに今の時刻と比べる)
    pub scout_banners: Vec<ScoutBanner>,
    /// ショップの商品(`shop_product_id`の順)
    pub shop_products: Vec<ShopProducts>,
}

impl MasterData {
    /// MySQLの各マスタテーブルから全件読み込む。
    ///
    /// # Errors
    /// DBアクセスに失敗した場合に`sqlx::Error`を返す。
    pub async fn load(pool: &MySqlPool) -> Result<Self, sqlx::Error> {
        let pachimon = load_pachimon(pool).await?;
        let move_group_moves = load_move_group_moves(pool).await?;
        let starter_party_slots = load_starter_party_slots(pool).await?;
        let items = load_items(pool).await?;
        let scout_banners = load_scout_banners(pool).await?;
        let shop_products = load_shop_products(pool).await?;

        Ok(MasterData {
            pachimon,
            move_group_moves,
            starter_party_slots,
            items,
            scout_banners,
            shop_products,
        })
    }
}

#[derive(sqlx::FromRow)]
struct PachimonRow {
    pachimon_id: i32,
    name: String,
    primary_type: u8,
    secondary_type: u8,
    base_hp: i32,
    base_atk: i32,
    base_def: i32,
    base_spatk: i32,
    base_spdef: i32,
    base_speed: i32,
    rarity: u8,
    move_group_id: i32,
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

#[derive(sqlx::FromRow)]
struct MoveGroupMovesRow {
    unique_id: i32,
    group_id: i32,
    move_id: i32,
    is_initial: bool,
}

async fn load_move_group_moves(pool: &MySqlPool) -> Result<Vec<MoveGroupMoves>, sqlx::Error> {
    let rows: Vec<MoveGroupMovesRow> = sqlx::query_as(
        "SELECT unique_id, group_id, move_id, is_initial FROM move_group_moves ORDER BY unique_id",
    )
    .fetch_all(pool)
    .await?;

    Ok(rows
        .into_iter()
        .map(|row| MoveGroupMoves {
            unique_id: row.unique_id,
            group_id: row.group_id,
            move_id: row.move_id,
            is_initial: row.is_initial,
        })
        .collect())
}

#[derive(sqlx::FromRow)]
struct StarterPartySlotsRow {
    slot_no: i32,
    pachimon_id: i32,
}

async fn load_starter_party_slots(pool: &MySqlPool) -> Result<Vec<StarterPartySlots>, sqlx::Error> {
    let rows: Vec<StarterPartySlotsRow> =
        sqlx::query_as("SELECT slot_no, pachimon_id FROM starter_party_slots ORDER BY slot_no")
            .fetch_all(pool)
            .await?;

    Ok(rows
        .into_iter()
        .map(|row| StarterPartySlots {
            slot_no: row.slot_no,
            pachimon_id: row.pachimon_id,
        })
        .collect())
}

#[derive(sqlx::FromRow)]
struct ItemsRow {
    item_id: i32,
    name: String,
}

async fn load_items(pool: &MySqlPool) -> Result<Vec<Items>, sqlx::Error> {
    let rows: Vec<ItemsRow> = sqlx::query_as("SELECT item_id, name FROM items ORDER BY item_id")
        .fetch_all(pool)
        .await?;

    Ok(rows
        .into_iter()
        .map(|row| Items {
            item_id: row.item_id,
            name: row.name,
        })
        .collect())
}

#[derive(sqlx::FromRow)]
struct ScoutBannersRow {
    banner_id: String,
    name: String,
    cost_per_roll: i32,
    start_at: chrono::NaiveDateTime,
    end_at: chrono::NaiveDateTime,
}

#[derive(sqlx::FromRow)]
struct ScoutBannerRatesRow {
    banner_id: String,
    rarity: u8,
    weight: i32,
}

#[derive(sqlx::FromRow)]
struct ShopProductsRow {
    shop_product_id: i32,
    item_id: i32,
    amount: i32,
    cost_item_id: i32,
    cost_amount: i32,
}

async fn load_shop_products(pool: &MySqlPool) -> Result<Vec<ShopProducts>, sqlx::Error> {
    let rows: Vec<ShopProductsRow> = sqlx::query_as(
        "SELECT shop_product_id, item_id, amount, cost_item_id, cost_amount FROM shop_products \
         ORDER BY shop_product_id",
    )
    .fetch_all(pool)
    .await?;

    Ok(rows
        .into_iter()
        .map(|row| ShopProducts {
            shop_product_id: row.shop_product_id,
            item_id: row.item_id,
            amount: row.amount,
            cost_item_id: row.cost_item_id,
            cost_amount: row.cost_amount,
        })
        .collect())
}

/// `scout_banners`と`scout_banner_rates`を読み、バナーごとに排出率をまとめる。日時はUTCで入っている。
async fn load_scout_banners(pool: &MySqlPool) -> Result<Vec<ScoutBanner>, sqlx::Error> {
    let banners: Vec<ScoutBannersRow> = sqlx::query_as(
        "SELECT banner_id, name, cost_per_roll, start_at, end_at FROM scout_banners \
         ORDER BY start_at, banner_id",
    )
    .fetch_all(pool)
    .await?;
    let rates: Vec<ScoutBannerRatesRow> = sqlx::query_as(
        "SELECT banner_id, rarity, weight FROM scout_banner_rates ORDER BY scout_banner_rate_id",
    )
    .fetch_all(pool)
    .await?;

    Ok(banners
        .into_iter()
        .map(|banner| ScoutBanner {
            rates: rates
                .iter()
                .filter(|rate| rate.banner_id == banner.banner_id)
                .map(|rate| ScoutBannerRate {
                    rarity: rarity_from_u8(rate.rarity),
                    weight: rate.weight,
                })
                .collect(),
            banner_id: banner.banner_id,
            name: banner.name,
            cost_per_roll: banner.cost_per_roll,
            start_at: banner.start_at,
            end_at: banner.end_at,
        })
        .collect())
}
