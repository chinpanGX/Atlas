use std::collections::HashSet;

use serde::{Deserialize, Serialize};
use sqlx::{MySql, MySqlPool, Transaction};
use ulid::Ulid;

use crate::error::AppError;
use crate::master::MasterData;
use crate::model::player_pachimon::{PlayerPachimon, PlayerPachimonMove};
use crate::model::player_party_slot::PlayerPartySlot;

/// `player_pachimon.effort_values`(JSON)の中身。キーは`grant`が書き込む形と同じ。
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct EffortValues {
    pub hp: i32,
    pub atk: i32,
    pub def: i32,
    pub spatk: i32,
    pub spdef: i32,
    pub speed: i32,
}

/// 対戦で1体を組み立てるための所持データ(`POST /internal/battle/loadouts`用)。
/// `moves`はslot順。
pub struct BattleLoadout {
    pub player_pachimon_id: String,
    pub pachimon_id: i32,
    pub effort_values: EffortValues,
    pub moves: Vec<PlayerPachimonMove>,
}

/// `PUT /players/me/party`リクエストの1slot分の入力。
pub struct PartySlotInput {
    pub slot: i32,
    pub player_pachimon_id: String,
}

/// `pachimon_id`の個体を1体生成し、`moves`(技グループの`is_initial`技)を初期技としてセットする。
/// スカウトでの入手(`scout_service::select_candidate`)・スターター編成の付与
/// (`player_service::create`)の両方から共通で使う。呼び出し元のトランザクション内で実行する。
///
/// 努力値(`effort_values`)は付与時点では常に全ステータス0とする(配分機能は未実装)。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn grant(
    tx: &mut Transaction<'_, MySql>,
    player_id: &str,
    pachimon_id: i32,
    moves: &[i32],
) -> Result<(PlayerPachimon, Vec<PlayerPachimonMove>), AppError> {
    let player_pachimon_id = Ulid::new().to_string();
    let effort_values =
        serde_json::json!({"hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 0});

    sqlx::query(
        "INSERT INTO player_pachimon (player_pachimon_id, player_id, pachimon_id, effort_values) \
         VALUES (?, ?, ?, ?)",
    )
    .bind(&player_pachimon_id)
    .bind(player_id)
    .bind(pachimon_id)
    .bind(sqlx::types::Json(&effort_values))
    .execute(&mut **tx)
    .await
    .map_err(|_| AppError::InternalError)?;

    let mut granted_moves = Vec::with_capacity(moves.len());
    for (slot, move_id) in moves.iter().enumerate() {
        let player_pachimon_move_id = Ulid::new().to_string();
        let slot = slot as i32 + 1;
        sqlx::query(
            "INSERT INTO player_pachimon_moves \
                (player_pachimon_move_id, player_pachimon_id, slot, move_id) \
             VALUES (?, ?, ?, ?)",
        )
        .bind(&player_pachimon_move_id)
        .bind(&player_pachimon_id)
        .bind(slot)
        .bind(*move_id)
        .execute(&mut **tx)
        .await
        .map_err(|_| AppError::InternalError)?;

        granted_moves.push(PlayerPachimonMove {
            player_pachimon_move_id,
            player_pachimon_id: player_pachimon_id.clone(),
            slot,
            move_id: *move_id,
        });
    }

    let obtained_at: (chrono::NaiveDateTime,) =
        sqlx::query_as("SELECT obtained_at FROM player_pachimon WHERE player_pachimon_id = ?")
            .bind(&player_pachimon_id)
            .fetch_one(&mut **tx)
            .await
            .map_err(|_| AppError::InternalError)?;

    Ok((
        PlayerPachimon {
            player_pachimon_id,
            player_id: player_id.to_string(),
            pachimon_id,
            obtained_at: obtained_at.0,
        },
        granted_moves,
    ))
}

/// 認証済みプレイヤーの所持パチモン一覧を取得する。覚えている技は[`list_owned_moves`]、
/// パーティ編成状況は[`list_party`]で別途取得する(`playerDiff`が`pachimon`/
/// `pachimonMoveMap`を独立したリソースとして扱うため、ここでもネストさせずフラットに返す)。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn list_owned_pachimon(
    pool: &MySqlPool,
    player_id: &str,
) -> Result<Vec<PlayerPachimon>, AppError> {
    let rows: Vec<(String, i32, chrono::NaiveDateTime)> = sqlx::query_as(
        "SELECT player_pachimon_id, pachimon_id, obtained_at FROM player_pachimon \
         WHERE player_id = ? ORDER BY obtained_at",
    )
    .bind(player_id)
    .fetch_all(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(rows
        .into_iter()
        .map(
            |(player_pachimon_id, pachimon_id, obtained_at)| PlayerPachimon {
                player_pachimon_id,
                player_id: player_id.to_string(),
                pachimon_id,
                obtained_at,
            },
        )
        .collect())
}

/// 認証済みプレイヤーが所持する全個体分の、覚えている技を`player_pachimon_id`込みでまとめて
/// 取得する(`player_pachimon`とJOINし1クエリで完結させる。個体ごとに問い合わせるN+1を避ける)。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn list_owned_moves(
    pool: &MySqlPool,
    player_id: &str,
) -> Result<Vec<PlayerPachimonMove>, AppError> {
    let rows: Vec<(String, String, i32, i32)> = sqlx::query_as(
        "SELECT ppm.player_pachimon_move_id, ppm.player_pachimon_id, ppm.slot, ppm.move_id \
         FROM player_pachimon_moves ppm \
         JOIN player_pachimon pp ON pp.player_pachimon_id = ppm.player_pachimon_id \
         WHERE pp.player_id = ? ORDER BY pp.obtained_at, ppm.slot",
    )
    .bind(player_id)
    .fetch_all(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(rows
        .into_iter()
        .map(
            |(player_pachimon_move_id, player_pachimon_id, slot, move_id)| PlayerPachimonMove {
                player_pachimon_move_id,
                player_pachimon_id,
                slot,
                move_id,
            },
        )
        .collect())
}

/// `player_id`が所持する個体のうち、`player_pachimon_ids`で指定したものを対戦用に取得する
/// (BattleServerが選出を受け取ったときに呼ぶ)。返り値は`player_pachimon_ids`と同じ順番。
///
/// # Errors
/// 指定した個体に`player_id`の所持でないもの(存在しないIDを含む)がある場合に
/// `AppError::NotFound`、DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn find_battle_loadouts(
    pool: &MySqlPool,
    player_id: &str,
    player_pachimon_ids: &[String],
) -> Result<Vec<BattleLoadout>, AppError> {
    let owned: Vec<(String, i32, sqlx::types::Json<EffortValues>)> = sqlx::query_as(
        "SELECT player_pachimon_id, pachimon_id, effort_values FROM player_pachimon \
         WHERE player_id = ?",
    )
    .bind(player_id)
    .fetch_all(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    // list_owned_movesはslot順に並んでいるため、個体ごとに振り分けてもslot順が保たれる。
    let owned_moves = list_owned_moves(pool, player_id).await?;

    player_pachimon_ids
        .iter()
        .map(|id| {
            let (player_pachimon_id, pachimon_id, effort_values) = owned
                .iter()
                .find(|(owned_id, _, _)| owned_id == id)
                .ok_or(AppError::NotFound)?;
            Ok(BattleLoadout {
                player_pachimon_id: player_pachimon_id.clone(),
                pachimon_id: *pachimon_id,
                effort_values: effort_values.0,
                moves: owned_moves
                    .iter()
                    .filter(|m| &m.player_pachimon_id == id)
                    .cloned()
                    .collect(),
            })
        })
        .collect()
}

/// 認証済みプレイヤーの現在のパーティ編成(`player_party_slots`)を取得する。
/// 割当が無いslotは行が存在しないため、返る件数は0-6件。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn list_party(
    pool: &MySqlPool,
    player_id: &str,
) -> Result<Vec<PlayerPartySlot>, AppError> {
    let rows: Vec<(String, i32, String)> = sqlx::query_as(
        "SELECT party_slot_id, slot, player_pachimon_id FROM player_party_slots \
         WHERE player_id = ? ORDER BY slot",
    )
    .bind(player_id)
    .fetch_all(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(rows
        .into_iter()
        .map(
            |(party_slot_id, slot, player_pachimon_id)| PlayerPartySlot {
                party_slot_id,
                player_id: player_id.to_string(),
                slot,
                player_pachimon_id,
            },
        )
        .collect())
}

/// バトル用パーティ(1-6体)を編成する。既存の割当(`player_party_slots`)を一旦全削除してから、
/// 指定された`player_pachimon_id`をそれぞれのslotへ新しいULIDで再割当する(全置き換え)。
/// 削除前の`party_slot_id`一覧も返す(`POST /edit/party`の`playerDiff.partySlots.removed`用)。
///
/// # Errors
/// 人数が1-6体でない、slot/`player_pachimon_id`が重複している場合に`AppError::BadRequest`、
/// 指定した`player_pachimon_id`が呼び出したプレイヤー自身の所持個体でない場合に
/// `AppError::NotFound`、DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn set_party(
    pool: &MySqlPool,
    player_id: &str,
    slots: Vec<PartySlotInput>,
) -> Result<(Vec<PlayerPartySlot>, Vec<String>), AppError> {
    if slots.is_empty() || slots.len() > 6 {
        return Err(AppError::BadRequest(
            "party size must be between 1 and 6".to_string(),
        ));
    }

    let mut seen_slots = HashSet::new();
    let mut seen_ids = HashSet::new();
    for s in &slots {
        if !(1..=6).contains(&s.slot) {
            return Err(AppError::BadRequest(
                "slot must be between 1 and 6".to_string(),
            ));
        }
        if !seen_slots.insert(s.slot) {
            return Err(AppError::BadRequest("duplicate slot".to_string()));
        }
        if !seen_ids.insert(s.player_pachimon_id.clone()) {
            return Err(AppError::BadRequest(
                "duplicate playerPachimonId".to_string(),
            ));
        }
    }

    let mut tx = pool.begin().await.map_err(|_| AppError::InternalError)?;

    for s in &slots {
        let owned: Option<(String,)> = sqlx::query_as(
            "SELECT player_pachimon_id FROM player_pachimon \
             WHERE player_pachimon_id = ? AND player_id = ?",
        )
        .bind(&s.player_pachimon_id)
        .bind(player_id)
        .fetch_optional(&mut *tx)
        .await
        .map_err(|_| AppError::InternalError)?;

        if owned.is_none() {
            return Err(AppError::NotFound);
        }
    }

    let removed: Vec<(String,)> =
        sqlx::query_as("SELECT party_slot_id FROM player_party_slots WHERE player_id = ?")
            .bind(player_id)
            .fetch_all(&mut *tx)
            .await
            .map_err(|_| AppError::InternalError)?;
    let removed: Vec<String> = removed.into_iter().map(|(id,)| id).collect();

    sqlx::query("DELETE FROM player_party_slots WHERE player_id = ?")
        .bind(player_id)
        .execute(&mut *tx)
        .await
        .map_err(|_| AppError::InternalError)?;

    let mut result = Vec::with_capacity(slots.len());
    for s in &slots {
        let party_slot_id = Ulid::new().to_string();

        sqlx::query(
            "INSERT INTO player_party_slots (party_slot_id, player_id, slot, player_pachimon_id) \
             VALUES (?, ?, ?, ?)",
        )
        .bind(&party_slot_id)
        .bind(player_id)
        .bind(s.slot)
        .bind(&s.player_pachimon_id)
        .execute(&mut *tx)
        .await
        .map_err(|_| AppError::InternalError)?;

        result.push(PlayerPartySlot {
            party_slot_id,
            player_id: player_id.to_string(),
            slot: s.slot,
            player_pachimon_id: s.player_pachimon_id.clone(),
        });
    }

    tx.commit().await.map_err(|_| AppError::InternalError)?;

    result.sort_by_key(|r| r.slot);
    Ok((result, removed))
}

/// 所持パチモンの技を付け替える。グループ内の候補技(`move_group_moves`)以外は指定できない。
///
/// # Errors
/// `slot`が1-4の範囲外の場合、`move_id`が対象パチモンの技グループの候補技でない場合に
/// `AppError::BadRequest`、指定した`player_pachimon_id`が呼び出したプレイヤー自身の
/// 所持個体でない場合に`AppError::NotFound`、DBアクセスに失敗した場合に
/// `AppError::InternalError`を返す。
pub async fn update_move(
    pool: &MySqlPool,
    master: &MasterData,
    player_id: &str,
    player_pachimon_id: &str,
    slot: i32,
    move_id: i32,
) -> Result<PlayerPachimonMove, AppError> {
    if !(1..=4).contains(&slot) {
        return Err(AppError::BadRequest(
            "slot must be between 1 and 4".to_string(),
        ));
    }

    let row: Option<(i32,)> = sqlx::query_as(
        "SELECT pachimon_id FROM player_pachimon WHERE player_pachimon_id = ? AND player_id = ?",
    )
    .bind(player_pachimon_id)
    .bind(player_id)
    .fetch_optional(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    let pachimon_id = row.ok_or(AppError::NotFound)?.0;

    let pachimon = master
        .pachimon
        .iter()
        .find(|p| p.pachimon_id == pachimon_id)
        .ok_or(AppError::InternalError)?;

    let is_candidate_move = master
        .move_group_moves
        .iter()
        .any(|m| m.group_id == pachimon.move_group_id && m.move_id == move_id);
    if !is_candidate_move {
        return Err(AppError::BadRequest(
            "moveId is not a candidate move for this pachimon".to_string(),
        ));
    }

    // 新規slotの場合のみ使われるULID。既存slotの更新時はON DUPLICATE KEY UPDATEの対象に
    // player_pachimon_move_idを含めないため、既存の値がそのまま維持される。
    let new_player_pachimon_move_id = Ulid::new().to_string();

    sqlx::query(
        "INSERT INTO player_pachimon_moves \
            (player_pachimon_move_id, player_pachimon_id, slot, move_id) \
         VALUES (?, ?, ?, ?) \
         ON DUPLICATE KEY UPDATE move_id = VALUES(move_id)",
    )
    .bind(&new_player_pachimon_move_id)
    .bind(player_pachimon_id)
    .bind(slot)
    .bind(move_id)
    .execute(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    let player_pachimon_move_id: (String,) = sqlx::query_as(
        "SELECT player_pachimon_move_id FROM player_pachimon_moves \
         WHERE player_pachimon_id = ? AND slot = ?",
    )
    .bind(player_pachimon_id)
    .bind(slot)
    .fetch_one(pool)
    .await
    .map_err(|_| AppError::InternalError)?;

    Ok(PlayerPachimonMove {
        player_pachimon_move_id: player_pachimon_move_id.0,
        player_pachimon_id: player_pachimon_id.to_string(),
        slot,
        move_id,
    })
}
