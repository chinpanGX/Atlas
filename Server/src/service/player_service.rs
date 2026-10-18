use chrono::Utc;
use sqlx::{MySql, MySqlExecutor, Transaction};
use ulid::Ulid;

use crate::error::AppError;
use crate::master::MasterData;
use crate::model::player::Player;
use crate::model::player_party_slot::PlayerPartySlot;
use crate::service::game_log::{GameLogCollector, PachimonReason, PartyReason};
use crate::service::{player_item_service, player_pachimon_service};

/// signup時にitem_id=1(ジェム)として初期付与する数量
/// (スカウトの紹介コスト150/回の2回分。初回起動時点でスカウトを試せるようにする)。
const STARTER_GEMS: i32 = 300;

/// デバイス登録で発行した`player_id`でプレイヤーを作成する。
///
/// 作成済みなら何も変えずに作成済みのプレイヤーを返す(サインアップは再送されても同じ結果になるよう、
/// 冪等に作る)。作成と同時に`starter_party_slots`マスタの内容をそのまま複製して初期パーティを付与し、
/// 対戦可能な状態でゲームを開始できるようにする。呼び出し元のトランザクションで行うので、
/// 途中で失敗しても「プレイヤーはいるがパーティが空」にはならない。初期の付与は、作ったときだけゲームログに記録する。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn create(
    tx: &mut Transaction<'_, MySql>,
    logs: &mut GameLogCollector,
    master: &MasterData,
    player_id: &str,
    nickname: &str,
) -> Result<Player, AppError> {
    // 先に作ってみて、作れたときだけ初期の付与をする。主キーが重複したら作成済みとみなし、何も変えない
    // (同じプレイヤーのサインアップが同時に届いても、後の方はその行のロックを待ってから重複になる)。
    // 「無いことを SELECT ... FOR UPDATE で確かめてから INSERT」にすると、行の無い範囲のロック(ギャップロック)を
    // 別のプレイヤーのサインアップと取り合ってデッドロックする。player_id は増える順の ULID なので、
    // 新しいプレイヤーはいつも表の末尾の同じ範囲に入り、同時のサインアップで起きやすい。
    // ON DUPLICATE KEY UPDATE は、sqlx が CLIENT_FOUND_ROWS で接続するため、作れたかを影響行数で見分けられない。
    let inserted = sqlx::query("INSERT INTO players (player_id, nickname) VALUES (?, ?)")
        .bind(player_id)
        .bind(nickname)
        .execute(&mut **tx)
        .await;
    match inserted {
        Ok(_) => {}
        Err(sqlx::Error::Database(e)) if e.is_unique_violation() => {
            let (player_id, nickname, created_at): (String, String, chrono::NaiveDateTime) =
                sqlx::query_as(
                    "SELECT player_id, nickname, created_at FROM players WHERE player_id = ?",
                )
                .bind(player_id)
                .fetch_one(&mut **tx)
                .await?;
            return Ok(Player {
                player_id,
                nickname,
                created_at,
            });
        }
        Err(e) => return Err(e.into()),
    }

    grant_starter_party(tx, logs, master, player_id).await?;

    player_item_service::grant_initial(
        tx,
        logs,
        player_id,
        player_item_service::GEM_ITEM_ID,
        STARTER_GEMS,
    )
    .await?;

    Ok(Player {
        player_id: player_id.to_string(),
        nickname: nickname.to_string(),
        created_at: Utc::now().naive_utc(),
    })
}

/// `starter_party_slots`マスタの各行を`player_pachimon`(+初期技)として生成し、
/// 対応する`slot_no`で`player_party_slots`へ割り当てる。付与した個体と編成をゲームログに記録する。
async fn grant_starter_party(
    tx: &mut Transaction<'_, MySql>,
    logs: &mut GameLogCollector,
    master: &MasterData,
    player_id: &str,
) -> Result<(), AppError> {
    let mut party = Vec::with_capacity(master.starter_party_slots.len());
    for slot in &master.starter_party_slots {
        let pachimon = master
            .pachimon
            .iter()
            .find(|p| p.pachimon_id == slot.pachimon_id)
            .ok_or_else(|| AppError::internal("starter party refers to an unknown pachimon"))?;

        let moves: Vec<i32> = master
            .move_group_moves
            .iter()
            .filter(|row| row.group_id == pachimon.move_group_id && row.is_initial)
            .map(|row| row.move_id)
            .collect();

        let (player_pachimon, moves) =
            player_pachimon_service::grant(tx, player_id, slot.pachimon_id, &moves).await?;
        logs.record_pachimon(&player_pachimon, &moves, PachimonReason::SignUp);

        let party_slot_id = Ulid::generate().to_string();
        sqlx::query(
            "INSERT INTO player_party_slots (party_slot_id, player_id, slot, player_pachimon_id) \
             VALUES (?, ?, ?, ?)",
        )
        .bind(&party_slot_id)
        .bind(player_id)
        .bind(slot.slot_no)
        .bind(&player_pachimon.player_pachimon_id)
        .execute(&mut **tx)
        .await?;

        party.push(PlayerPartySlot {
            party_slot_id,
            player_id: player_id.to_string(),
            slot: slot.slot_no,
            player_pachimon_id: player_pachimon.player_pachimon_id,
        });
    }

    logs.record_party(player_id, &party, PartyReason::SignUp);
    Ok(())
}

/// `player_id`のプレイヤーを取得する。まだ作られていなければ`None`を返す。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn find_by_id(
    executor: impl MySqlExecutor<'_>,
    player_id: &str,
) -> Result<Option<Player>, AppError> {
    let row: Option<(String, String, chrono::NaiveDateTime)> =
        sqlx::query_as("SELECT player_id, nickname, created_at FROM players WHERE player_id = ?")
            .bind(player_id)
            .fetch_optional(executor)
            .await?;

    Ok(row.map(|(player_id, nickname, created_at)| Player {
        player_id,
        nickname,
        created_at,
    }))
}
