use std::collections::HashSet;

use rand::seq::IndexedRandom;
use sqlx::types::Json;
use sqlx::{MySql, MySqlExecutor, MySqlPool, Transaction};
use ulid::Ulid;

use voltech_grpc_error::Internal;

use crate::error::AppError;
use crate::master::MasterData;
use crate::model::player_pachimon::{PlayerPachimon, PlayerPachimonMove, StatPoints};
use crate::model::player_party_slot::PlayerPartySlot;
use crate::service::game_log::{GameLogCollector, ItemReason, PachimonReason, PartyReason};
use crate::service::player_diff::PlayerDiffCollector;
use crate::service::player_item_service;

/// 技の付け替え1回で使う育成チケットの枚数。
pub const MOVE_CHANGE_TICKET_COST: i32 = 5;

/// 対戦で1体を組み立てるための所持データ(内部APIの`GetLoadouts`用)。
/// `moves`はslot順。
pub struct BattleLoadout {
    pub player_pachimon_id: String,
    pub pachimon_id: i32,
    pub stat_points: StatPoints,
    pub moves: Vec<PlayerPachimonMove>,
}

/// 技の付け替えの入力。`player_pachimon_id`の個体の`slot`(1-4)に`move_id`を入れる。
pub struct MoveSlotInput {
    pub player_pachimon_id: String,
    pub slot: i32,
    pub move_id: i32,
}

/// パーティ編成の1slot分の入力。
pub struct PartySlotInput {
    pub slot: i32,
    pub player_pachimon_id: String,
}

/// `pachimon_id`の個体を1体生成し、`moves`(技グループの`is_initial`技)を初期技としてセットする。
/// スカウトでの入手(`scout_service::select_candidate`)・スターター編成の付与
/// (`player_service::create`)の両方から共通で使う。呼び出し元のトランザクション内で実行する。
///
/// ステータスポイントは入手したときは全能力0。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn grant(
    tx: &mut Transaction<'_, MySql>,
    player_id: &str,
    pachimon_id: i32,
    moves: &[i32],
) -> Result<(PlayerPachimon, Vec<PlayerPachimonMove>), AppError> {
    let player_pachimon_id = Ulid::generate().to_string();
    let stat_points = StatPoints::default();

    sqlx::query(
        "INSERT INTO player_pachimon (player_pachimon_id, player_id, pachimon_id, effort_values) \
         VALUES (?, ?, ?, ?)",
    )
    .bind(&player_pachimon_id)
    .bind(player_id)
    .bind(pachimon_id)
    .bind(Json(stat_points))
    .execute(&mut **tx)
    .await?;

    let mut granted_moves = Vec::with_capacity(moves.len());
    for (slot, move_id) in moves.iter().enumerate() {
        let player_pachimon_move_id = Ulid::generate().to_string();
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
        .await?;

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
            .await?;

    Ok((
        PlayerPachimon {
            player_pachimon_id,
            player_id: player_id.to_string(),
            pachimon_id,
            obtained_at: obtained_at.0,
            stat_points,
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
    executor: impl MySqlExecutor<'_>,
    player_id: &str,
) -> Result<Vec<PlayerPachimon>, AppError> {
    let rows: Vec<(String, i32, chrono::NaiveDateTime, Json<StatPoints>)> = sqlx::query_as(
        "SELECT player_pachimon_id, pachimon_id, obtained_at, effort_values FROM player_pachimon \
         WHERE player_id = ? ORDER BY obtained_at",
    )
    .bind(player_id)
    .fetch_all(executor)
    .await?;

    Ok(rows
        .into_iter()
        .map(
            |(player_pachimon_id, pachimon_id, obtained_at, Json(stat_points))| PlayerPachimon {
                player_pachimon_id,
                player_id: player_id.to_string(),
                pachimon_id,
                obtained_at,
                stat_points,
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
    executor: impl MySqlExecutor<'_>,
    player_id: &str,
) -> Result<Vec<PlayerPachimonMove>, AppError> {
    let rows: Vec<(String, String, i32, i32)> = sqlx::query_as(
        "SELECT ppm.player_pachimon_move_id, ppm.player_pachimon_id, ppm.slot, ppm.move_id \
         FROM player_pachimon_moves ppm \
         JOIN player_pachimon pp ON pp.player_pachimon_id = ppm.player_pachimon_id \
         WHERE pp.player_id = ? ORDER BY pp.obtained_at, ppm.slot",
    )
    .bind(player_id)
    .fetch_all(executor)
    .await?;

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
    let owned: Vec<(String, i32, Json<StatPoints>)> = sqlx::query_as(
        "SELECT player_pachimon_id, pachimon_id, effort_values FROM player_pachimon \
         WHERE player_id = ?",
    )
    .bind(player_id)
    .fetch_all(pool)
    .await?;

    // list_owned_movesはslot順に並んでいるため、個体ごとに振り分けてもslot順が保たれる。
    let owned_moves = list_owned_moves(pool, player_id).await?;

    player_pachimon_ids
        .iter()
        .map(|id| {
            let (player_pachimon_id, pachimon_id, stat_points) = owned
                .iter()
                .find(|(owned_id, _, _)| owned_id == id)
                .ok_or(AppError::NotFound)?;
            Ok(BattleLoadout {
                player_pachimon_id: player_pachimon_id.clone(),
                pachimon_id: *pachimon_id,
                stat_points: stat_points.0,
                moves: owned_moves
                    .iter()
                    .filter(|m| &m.player_pachimon_id == id)
                    .cloned()
                    .collect(),
            })
        })
        .collect()
}

/// 対戦の選出候補(パーティ編成中の1体)。
pub struct BattlePartyMember {
    pub player_pachimon_id: String,
    pub pachimon_id: i32,
}

/// `player_id`の現在のパーティ編成を、slot順にどのパチモンかと合わせて取得する
/// (BattleServerが選出フェーズの開始時に呼ぶ)。未編成のslotは含まれないため0-6件。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn list_battle_party(
    pool: &MySqlPool,
    player_id: &str,
) -> Result<Vec<BattlePartyMember>, AppError> {
    let rows: Vec<(String, i32)> = sqlx::query_as(
        "SELECT pp.player_pachimon_id, pp.pachimon_id FROM player_party_slots ps          JOIN player_pachimon pp ON pp.player_pachimon_id = ps.player_pachimon_id          WHERE ps.player_id = ? ORDER BY ps.slot",
    )
    .bind(player_id)
    .fetch_all(pool)
    .await?;

    Ok(rows
        .into_iter()
        .map(|(player_pachimon_id, pachimon_id)| BattlePartyMember {
            player_pachimon_id,
            pachimon_id,
        })
        .collect())
}

/// 認証済みプレイヤーの現在のパーティ編成(`player_party_slots`)を取得する。
/// 割当が無いslotは行が存在しないため、返る件数は0-6件。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn list_party(
    executor: impl MySqlExecutor<'_>,
    player_id: &str,
) -> Result<Vec<PlayerPartySlot>, AppError> {
    let rows: Vec<(String, i32, String)> = sqlx::query_as(
        "SELECT party_slot_id, slot, player_pachimon_id FROM player_party_slots \
         WHERE player_id = ? ORDER BY slot",
    )
    .bind(player_id)
    .fetch_all(executor)
    .await?;

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

/// パーティの編成・育成(技の付け替え・ステータスポイントの割り振り)の失敗。
#[derive(Debug)]
pub enum PartyError {
    /// 人数・枠の番号・重複が規則に合わない。中身はログ用の理由。
    InvalidParty(&'static str),
    /// 指定した個体が呼び出したプレイヤー自身の所持個体でない(存在しないIDを含む)。
    PachimonNotOwned,
    /// 技の枠が1-4でない。
    InvalidMoveSlot,
    /// 技が個体の技グループの候補技でない。
    MoveNotLearnable,
    /// 技の枠に今と同じ技が入っている。
    MoveUnchanged,
    /// 別の枠でその技を覚えている。
    MoveAlreadyLearned,
    /// ステータスポイントに負の値がある、または合計が上限を超えている。
    InvalidStatPoints,
    /// 育成チケットが足りない。
    InsufficientTrainingTickets,
    /// DBアクセスやマスタの不整合。
    Internal(Internal),
}

impl From<AppError> for PartyError {
    #[track_caller]
    fn from(err: AppError) -> Self {
        Self::Internal(Internal::from(err))
    }
}

impl<E> From<E> for PartyError
where
    E: std::error::Error + Send + Sync + 'static,
{
    #[track_caller]
    fn from(err: E) -> Self {
        Self::Internal(Internal::from(err))
    }
}

/// バトル用パーティ(1-6体)を編成する。既存の割当(`player_party_slots`)を一旦全削除してから、
/// 指定された`player_pachimon_id`をそれぞれのslotへ新しいULIDで再割当する(全置き換え)。
/// 解除した割当と新しい割当を`diff`に、変更後の編成をゲームログに記録する。呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// 人数が1-6体でない、slotが1-6でない、slot/`player_pachimon_id`が重複している場合に`PartyError::InvalidParty`、
/// 指定した`player_pachimon_id`が呼び出したプレイヤー自身の所持個体でない場合に`PartyError::PachimonNotOwned`、
/// DBアクセスに失敗した場合に`PartyError::Internal`を返す。
pub async fn set_party(
    tx: &mut Transaction<'_, MySql>,
    diff: &mut PlayerDiffCollector,
    logs: &mut GameLogCollector,
    player_id: &str,
    slots: Vec<PartySlotInput>,
) -> Result<(), PartyError> {
    if slots.is_empty() || slots.len() > 6 {
        return Err(PartyError::InvalidParty(
            "party size must be between 1 and 6",
        ));
    }

    let mut seen_slots = HashSet::new();
    let mut seen_ids = HashSet::new();
    for s in &slots {
        if !(1..=6).contains(&s.slot) {
            return Err(PartyError::InvalidParty("slot must be between 1 and 6"));
        }
        if !seen_slots.insert(s.slot) {
            return Err(PartyError::InvalidParty("duplicate slot"));
        }
        if !seen_ids.insert(s.player_pachimon_id.clone()) {
            return Err(PartyError::InvalidParty("duplicate player_pachimon_id"));
        }
    }

    for s in &slots {
        let owned: Option<(String,)> = sqlx::query_as(
            "SELECT player_pachimon_id FROM player_pachimon \
             WHERE player_pachimon_id = ? AND player_id = ?",
        )
        .bind(&s.player_pachimon_id)
        .bind(player_id)
        .fetch_optional(&mut **tx)
        .await?;

        if owned.is_none() {
            return Err(PartyError::PachimonNotOwned);
        }
    }

    let removed: Vec<(String,)> =
        sqlx::query_as("SELECT party_slot_id FROM player_party_slots WHERE player_id = ?")
            .bind(player_id)
            .fetch_all(&mut **tx)
            .await?;
    for (party_slot_id,) in removed {
        diff.record_removed_party_slot(party_slot_id);
    }

    sqlx::query("DELETE FROM player_party_slots WHERE player_id = ?")
        .bind(player_id)
        .execute(&mut **tx)
        .await?;

    let mut party = Vec::with_capacity(slots.len());
    for s in slots {
        let party_slot_id = Ulid::generate().to_string();

        sqlx::query(
            "INSERT INTO player_party_slots (party_slot_id, player_id, slot, player_pachimon_id) \
             VALUES (?, ?, ?, ?)",
        )
        .bind(&party_slot_id)
        .bind(player_id)
        .bind(s.slot)
        .bind(&s.player_pachimon_id)
        .execute(&mut **tx)
        .await?;

        party.push(PlayerPartySlot {
            party_slot_id,
            player_id: player_id.to_string(),
            slot: s.slot,
            player_pachimon_id: s.player_pachimon_id,
        });
    }

    logs.record_party(player_id, &party, PartyReason::SetParty);
    for slot in party {
        diff.record_party_slot(slot);
    }
    Ok(())
}

/// ランダム編成で組むパーティの人数。
const RANDOM_PARTY_SIZE: usize = 6;

/// 1体が覚えている技の最大数(slot 1-4)。
const MAX_MOVES: usize = 4;

/// 開発用(対戦相手ボット向け)。pachimonマスタから重複なしで6体をランダムに選んで新たに付与し、
/// 各個体の技は技グループの候補技(`move_group_moves`)からランダムに最大4つ選ぶ。
/// 既存のパーティ編成は全て解除し、付与した6体でslot 1-6を編成し直す
/// (以前の所持個体は所持したまま残る)。付与した個体・技と、解除・追加した枠を`diff`に、
/// 付与した個体と変更後の編成をゲームログに記録する。
/// 呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// DBアクセスに失敗した場合に`AppError::InternalError`を返す。
pub async fn randomize_party(
    tx: &mut Transaction<'_, MySql>,
    diff: &mut PlayerDiffCollector,
    logs: &mut GameLogCollector,
    master: &MasterData,
    player_id: &str,
) -> Result<(), AppError> {
    // ThreadRngはSendでないため、await前に抽選を済ませてスコープを抜けておく
    let picks: Vec<(i32, Vec<i32>)> = {
        let mut rng = rand::rng();
        master
            .pachimon
            .sample(&mut rng, RANDOM_PARTY_SIZE)
            .map(|pachimon| {
                let mut candidates: Vec<i32> = master
                    .move_group_moves
                    .iter()
                    .filter(|row| row.group_id == pachimon.move_group_id)
                    .map(|row| row.move_id)
                    .collect();
                candidates.sort_unstable();
                candidates.dedup();
                let moves = candidates.sample(&mut rng, MAX_MOVES).copied().collect();
                (pachimon.pachimon_id, moves)
            })
            .collect()
    };

    let removed: Vec<(String,)> =
        sqlx::query_as("SELECT party_slot_id FROM player_party_slots WHERE player_id = ?")
            .bind(player_id)
            .fetch_all(&mut **tx)
            .await?;
    for (party_slot_id,) in removed {
        diff.record_removed_party_slot(party_slot_id);
    }

    sqlx::query("DELETE FROM player_party_slots WHERE player_id = ?")
        .bind(player_id)
        .execute(&mut **tx)
        .await?;

    let mut party = Vec::with_capacity(picks.len());
    for (index, (pachimon_id, moves)) in picks.iter().enumerate() {
        let (player_pachimon, granted_moves) = grant(tx, player_id, *pachimon_id, moves).await?;

        let party_slot_id = Ulid::generate().to_string();
        let slot = index as i32 + 1;
        sqlx::query(
            "INSERT INTO player_party_slots (party_slot_id, player_id, slot, player_pachimon_id) \
             VALUES (?, ?, ?, ?)",
        )
        .bind(&party_slot_id)
        .bind(player_id)
        .bind(slot)
        .bind(&player_pachimon.player_pachimon_id)
        .execute(&mut **tx)
        .await?;

        party.push(PlayerPartySlot {
            party_slot_id,
            player_id: player_id.to_string(),
            slot,
            player_pachimon_id: player_pachimon.player_pachimon_id.clone(),
        });
        logs.record_pachimon(&player_pachimon, &granted_moves, PachimonReason::Debug);
        diff.record_pachimon(player_pachimon);
        for granted_move in granted_moves {
            diff.record_pachimon_move(granted_move);
        }
    }

    logs.record_party(player_id, &party, PartyReason::Debug);
    for slot in party {
        diff.record_party_slot(slot);
    }
    Ok(())
}

/// 所持パチモンの技を付け替え、育成チケットを`MOVE_CHANGE_TICKET_COST`枚使う。グループ内の候補技
/// (`move_group_moves`)以外は指定できない。付け替えた割当と使った後のチケットを`diff`に、前後の技とチケットの消費を
/// ゲームログに記録する。呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// 次の順に確かめ、当てはまればチケットを使わずに返す。`slot`が1-4の範囲外の場合に`PartyError::InvalidMoveSlot`、
/// 指定した`player_pachimon_id`が呼び出したプレイヤー自身の所持個体でない場合に`PartyError::PachimonNotOwned`、
/// `move_id`が対象パチモンの技グループの候補技でない場合に`PartyError::MoveNotLearnable`、
/// その枠に同じ技が入っている場合に`PartyError::MoveUnchanged`、別の枠でその技を覚えている場合に
/// `PartyError::MoveAlreadyLearned`、チケットが足りない場合に`PartyError::InsufficientTrainingTickets`、
/// DBアクセスに失敗した場合に`PartyError::Internal`を返す。
pub async fn update_move(
    tx: &mut Transaction<'_, MySql>,
    diff: &mut PlayerDiffCollector,
    logs: &mut GameLogCollector,
    master: &MasterData,
    player_id: &str,
    input: MoveSlotInput,
) -> Result<(), PartyError> {
    let MoveSlotInput {
        player_pachimon_id,
        slot,
        move_id,
    } = input;
    if !(1..=4).contains(&slot) {
        return Err(PartyError::InvalidMoveSlot);
    }

    // 同じ個体を同時に付け替えて、同じ技が2つの枠に入らないよう、個体の行をロックしてから覚えている技を確かめる
    let row: Option<(i32,)> = sqlx::query_as(
        "SELECT pachimon_id FROM player_pachimon \
         WHERE player_pachimon_id = ? AND player_id = ? FOR UPDATE",
    )
    .bind(&player_pachimon_id)
    .bind(player_id)
    .fetch_optional(&mut **tx)
    .await?;

    let pachimon_id = row.ok_or(PartyError::PachimonNotOwned)?.0;

    let pachimon = master
        .pachimon
        .iter()
        .find(|p| p.pachimon_id == pachimon_id)
        .ok_or_else(|| {
            PartyError::Internal(Internal::msg(
                "player_pachimon refers to an unknown pachimon",
            ))
        })?;

    let is_candidate_move = master
        .move_group_moves
        .iter()
        .any(|m| m.group_id == pachimon.move_group_id && m.move_id == move_id);
    if !is_candidate_move {
        return Err(PartyError::MoveNotLearnable);
    }

    let learned: Vec<(i32, i32)> = sqlx::query_as(
        "SELECT slot, move_id FROM player_pachimon_moves WHERE player_pachimon_id = ?",
    )
    .bind(&player_pachimon_id)
    .fetch_all(&mut **tx)
    .await?;

    let before = learned
        .iter()
        .find(|(learned_slot, _)| *learned_slot == slot)
        .map(|(_, learned_move)| *learned_move);
    if before == Some(move_id) {
        return Err(PartyError::MoveUnchanged);
    }
    if learned
        .iter()
        .any(|(learned_slot, learned_move)| *learned_slot != slot && *learned_move == move_id)
    {
        return Err(PartyError::MoveAlreadyLearned);
    }

    let tickets = player_item_service::deduct(
        tx,
        logs,
        player_id,
        player_item_service::TRAINING_TICKET_ITEM_ID,
        MOVE_CHANGE_TICKET_COST,
        ItemReason::SetPachimonMove,
    )
    .await?
    .ok_or(PartyError::InsufficientTrainingTickets)?;
    diff.record_item(tickets);

    // 新規slotの場合のみ使われるULID。既存slotの更新時はON DUPLICATE KEY UPDATEの対象に
    // player_pachimon_move_idを含めないため、既存の値がそのまま維持される。
    let new_player_pachimon_move_id = Ulid::generate().to_string();

    sqlx::query(
        "INSERT INTO player_pachimon_moves \
            (player_pachimon_move_id, player_pachimon_id, slot, move_id) \
         VALUES (?, ?, ?, ?) AS new \
         ON DUPLICATE KEY UPDATE move_id = new.move_id",
    )
    .bind(&new_player_pachimon_move_id)
    .bind(&player_pachimon_id)
    .bind(slot)
    .bind(move_id)
    .execute(&mut **tx)
    .await?;

    let player_pachimon_move_id: (String,) = sqlx::query_as(
        "SELECT player_pachimon_move_id FROM player_pachimon_moves \
         WHERE player_pachimon_id = ? AND slot = ?",
    )
    .bind(&player_pachimon_id)
    .bind(slot)
    .fetch_one(&mut **tx)
    .await?;

    let after = PlayerPachimonMove {
        player_pachimon_move_id: player_pachimon_move_id.0,
        player_pachimon_id,
        slot,
        move_id,
    };
    logs.record_pachimon_move(player_id, &after, before);
    diff.record_pachimon_move(after);
    Ok(())
}

/// 所持パチモンのステータスポイントの割り振りを`stat_points`に変え、増やしたポイントの合計の数だけ育成チケットを使う
/// (減らすのは無料)。今と同じ割り振りなら何もしない。変えた個体と使った後のチケットを`diff`に、前後の割り振りと
/// チケットの消費をゲームログに記録する。呼び出し元のトランザクション内で実行する。
///
/// # Errors
/// 次の順に確かめ、当てはまれば何も変えずに返す。負の値がある・合計が上限を超える場合に`PartyError::InvalidStatPoints`、
/// 指定した`player_pachimon_id`が呼び出したプレイヤー自身の所持個体でない場合に`PartyError::PachimonNotOwned`、
/// チケットが足りない場合に`PartyError::InsufficientTrainingTickets`、DBアクセスに失敗した場合に`PartyError::Internal`を返す。
pub async fn set_stat_points(
    tx: &mut Transaction<'_, MySql>,
    diff: &mut PlayerDiffCollector,
    logs: &mut GameLogCollector,
    player_id: &str,
    player_pachimon_id: &str,
    stat_points: StatPoints,
) -> Result<(), PartyError> {
    if !stat_points.is_valid() {
        return Err(PartyError::InvalidStatPoints);
    }

    // 同時に割り振ったときに、どちらも同じ「今の値」から枚数を数えてチケットが足りなくならないよう、行をロックしてから読む
    let row: Option<(i32, chrono::NaiveDateTime, Json<StatPoints>)> = sqlx::query_as(
        "SELECT pachimon_id, obtained_at, effort_values FROM player_pachimon \
         WHERE player_pachimon_id = ? AND player_id = ? FOR UPDATE",
    )
    .bind(player_pachimon_id)
    .bind(player_id)
    .fetch_optional(&mut **tx)
    .await?;
    let (pachimon_id, obtained_at, Json(before)) = row.ok_or(PartyError::PachimonNotOwned)?;

    if before == stat_points {
        return Ok(());
    }

    let cost = before.increase_to(stat_points);
    if cost > 0 {
        let tickets = player_item_service::deduct(
            tx,
            logs,
            player_id,
            player_item_service::TRAINING_TICKET_ITEM_ID,
            cost,
            ItemReason::SetPachimonStatPoints,
        )
        .await?
        .ok_or(PartyError::InsufficientTrainingTickets)?;
        diff.record_item(tickets);
    }

    sqlx::query("UPDATE player_pachimon SET effort_values = ? WHERE player_pachimon_id = ?")
        .bind(Json(stat_points))
        .bind(player_pachimon_id)
        .execute(&mut **tx)
        .await?;

    logs.record_stat_points(player_id, player_pachimon_id, before, stat_points);
    diff.record_pachimon(PlayerPachimon {
        player_pachimon_id: player_pachimon_id.to_string(),
        player_id: player_id.to_string(),
        pachimon_id,
        obtained_at,
        stat_points,
    });
    Ok(())
}
