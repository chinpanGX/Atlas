use sqlx::{MySql, MySqlPool, Transaction};

use crate::error::AppError;
use crate::model::player_item::PlayerItem;

/// ジェム(スカウトの紹介コストに使う課金通貨相当)の`item_id`。
/// `Shared/master-data/csv/items_master.csv`で`item_id=1`として定義されている。
pub const GEM_ITEM_ID: i32 = 1;

/// 新規行としてアイテムを付与する(`player_service::create`のsignup時初期付与のみで使う想定)。
/// 呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// DBアクセスに失敗した場合(既に行が存在する場合の一意制約違反を含む)に
/// `AppError::InternalError`を返す。
pub async fn grant_initial(
    tx: &mut Transaction<'_, MySql>,
    player_id: &str,
    item_id: i32,
    quantity: i32,
) -> Result<(), AppError> {
    sqlx::query("INSERT INTO player_items (player_id, item_id, quantity) VALUES (?, ?, ?)")
        .bind(player_id)
        .bind(item_id)
        .bind(quantity)
        .execute(&mut **tx)
        .await
        .map_err(|_| AppError::InternalError)?;

    Ok(())
}

/// 指定アイテムを`amount`分加算する(`battle_service::record_result`の勝利報酬付与から利用)。
/// 行が無ければ`amount`個で作成する(UPSERT)。呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn add(
    tx: &mut Transaction<'_, MySql>,
    player_id: &str,
    item_id: i32,
    amount: i32,
) -> Result<(), AppError> {
    sqlx::query(
        "INSERT INTO player_items (player_id, item_id, quantity) VALUES (?, ?, ?) \
         ON DUPLICATE KEY UPDATE quantity = quantity + VALUES(quantity)",
    )
    .bind(player_id)
    .bind(item_id)
    .bind(amount)
    .execute(&mut **tx)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(())
}

/// 認証済みプレイヤーの所持アイテム一覧を取得する。所持数0のアイテムは行自体が存在しないため
/// 返る件数はプレイヤーが実際に所持しているアイテム種別数分のみ。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn list(pool: &MySqlPool, player_id: &str) -> Result<Vec<PlayerItem>, AppError> {
    let rows: Vec<(String, i32, i32)> = sqlx::query_as(
        "SELECT player_id, item_id, quantity FROM player_items \
         WHERE player_id = ? ORDER BY item_id",
    )
    .bind(player_id)
    .fetch_all(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(rows
        .into_iter()
        .map(|(player_id, item_id, quantity)| PlayerItem {
            player_id,
            item_id,
            quantity,
        })
        .collect())
}

/// 指定アイテムを`amount`分消費する(`scout_service::create_roll`のgems消費から利用)。
/// `WHERE quantity >= amount`を満たす行のみUPDATEすることで、所持数不足を1クエリで
/// 原子的に検出する(呼び出し元のトランザクション内で実行する)。
///
/// # Errors
/// 所持数が不足している場合(対象行が無い場合を含む)に`AppError::BadRequest`、
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn deduct(
    tx: &mut Transaction<'_, MySql>,
    player_id: &str,
    item_id: i32,
    amount: i32,
) -> Result<PlayerItem, AppError> {
    let update_result = sqlx::query(
        "UPDATE player_items SET quantity = quantity - ? \
         WHERE player_id = ? AND item_id = ? AND quantity >= ?",
    )
    .bind(amount)
    .bind(player_id)
    .bind(item_id)
    .bind(amount)
    .execute(&mut **tx)
    .await
    .map_err(|_| AppError::InternalError)?;

    if update_result.rows_affected() == 0 {
        return Err(AppError::BadRequest("insufficient items".to_string()));
    }

    let row: (String, i32, i32) = sqlx::query_as(
        "SELECT player_id, item_id, quantity FROM player_items \
         WHERE player_id = ? AND item_id = ?",
    )
    .bind(player_id)
    .bind(item_id)
    .fetch_one(&mut **tx)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(PlayerItem {
        player_id: row.0,
        item_id: row.1,
        quantity: row.2,
    })
}
