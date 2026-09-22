use chrono::Utc;
use sqlx::{MySql, MySqlPool, Transaction};
use ulid::Ulid;

use crate::error::AppError;
use crate::master::MasterData;
use crate::model::player::Player;
use crate::service::{player_item_service, player_pachimon_service};

/// signup時にitem_id=1(ジェム)として初期付与する数量
/// (`Shared/docs/design/outgame.md`「報酬設計(gems)」参照。スカウトの紹介コスト150/回の2回分)。
const STARTER_GEMS: i32 = 300;

/// 新しいプレイヤーを作成し、デバイスに紐付ける。
///
/// 1つの`device_id`につきプレイヤーは1件のみ(`players.device_id`はUNIQUE)。
/// 既に作成済みの`device_id`で呼び出された場合はUNIQUE制約違反となる。
/// 作成と同時に`starter_party_slots`マスタの内容をそのまま複製して初期パーティを付与し、
/// 対戦可能な状態でゲームを開始できるようにする(`Shared/docs/design/outgame.md`参照)。
/// プレイヤー作成〜初期パーティ付与までは1トランザクションで行い、途中失敗時は
/// 「プレイヤーはいるがパーティが空」という不整合を防ぐ。
///
/// # Arguments
/// * `pool` - MySQLへのコネクションプール
/// * `master` - メモリキャッシュされたマスタデータ(`starter_party_slots`等を参照する)
/// * `device_id` - 認証済みデバイスID
/// * `nickname` - プレイヤー名
///
/// # Errors
/// 既にプレイヤーが作成済みの場合に`AppError::Conflict`を返す。
/// DBアクセスに失敗した場合は`AppError::InternalError`を返す。
pub async fn create(
    pool: &MySqlPool,
    master: &MasterData,
    device_id: &str,
    nickname: &str,
) -> Result<Player, AppError> {
    let player_id = Ulid::new().to_string();

    let mut tx = pool.begin().await.map_err(|_| AppError::InternalError)?;

    sqlx::query("INSERT INTO players (player_id, device_id, nickname) VALUES (?, ?, ?)")
        .bind(&player_id)
        .bind(device_id)
        .bind(nickname)
        .execute(&mut *tx)
        .await
        .map_err(|err| match &err {
            sqlx::Error::Database(db_err) if db_err.is_unique_violation() => AppError::Conflict,
            _ => AppError::InternalError,
        })?;

    grant_starter_party(&mut tx, master, &player_id).await?;

    player_item_service::grant_initial(
        &mut tx,
        &player_id,
        player_item_service::GEM_ITEM_ID,
        STARTER_GEMS,
    )
    .await?;

    tx.commit().await.map_err(|_| AppError::InternalError)?;

    Ok(Player {
        player_id,
        device_id: device_id.to_string(),
        nickname: nickname.to_string(),
        created_at: Utc::now().naive_utc(),
    })
}

/// `starter_party_slots`マスタの各行を`player_pachimon`(+初期技)として生成し、
/// 対応する`slot_no`で`player_party_slots`へ割り当てる。
async fn grant_starter_party(
    tx: &mut Transaction<'_, MySql>,
    master: &MasterData,
    player_id: &str,
) -> Result<(), AppError> {
    for slot in &master.starter_party_slots {
        let pachimon = master
            .pachimon
            .iter()
            .find(|p| p.pachimon_id == slot.pachimon_id)
            .ok_or(AppError::InternalError)?;

        let moves: Vec<i64> = master
            .move_group_moves
            .iter()
            .filter(|row| row.group_id == pachimon.move_group_id && row.is_initial)
            .map(|row| row.move_id)
            .collect();

        let (player_pachimon, _moves) =
            player_pachimon_service::grant(tx, player_id, slot.pachimon_id, &moves).await?;

        let party_slot_id = Ulid::new().to_string();
        sqlx::query(
            "INSERT INTO player_party_slots (party_slot_id, player_id, slot, player_pachimon_id) \
             VALUES (?, ?, ?, ?)",
        )
        .bind(&party_slot_id)
        .bind(player_id)
        .bind(slot.slot_no as i32)
        .bind(&player_pachimon.player_pachimon_id)
        .execute(&mut **tx)
        .await
        .map_err(|_| AppError::InternalError)?;
    }

    Ok(())
}

/// `device_id`に紐づくプレイヤーを取得する。
///
/// # Errors
/// プレイヤーが未作成の場合に`AppError::NotFound`を返す。
/// DBアクセスに失敗した場合は`AppError::InternalError`を返す。
pub async fn find_by_device_id(pool: &MySqlPool, device_id: &str) -> Result<Player, AppError> {
    let row: Option<(String, String, String, chrono::NaiveDateTime)> = sqlx::query_as(
        "SELECT player_id, device_id, nickname, created_at FROM players WHERE device_id = ?",
    )
    .bind(device_id)
    .fetch_optional(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    let (player_id, device_id, nickname, created_at) = row.ok_or(AppError::NotFound)?;

    Ok(Player {
        player_id,
        device_id,
        nickname,
        created_at,
    })
}
