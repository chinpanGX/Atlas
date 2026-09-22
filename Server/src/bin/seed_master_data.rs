// マスタデータ投入コマンド。
//
// master-data-pipelineが生成したmaster_data/*.jsonをMySQLへUPSERTする。APIサーバー自体は
// 起動時にDBからマスタを読み込みメモリキャッシュを参照する設計のため、マスタ更新時は
// このコマンドを実行してDBへ反映したのち、サーバーを再起動して反映する(詳細は
// Shared/docs/design/architecture.mdの「マスターデータ運用」参照)。
//
// move_groups → moves → move_group_moves → pachimon → starter_party_slotsの順で投入する
// (move_group_moves/pachimon/starter_party_slotsの外部キー制約を満たすため)。
//
// 実行方法: cargo run --bin seed_master_data
use sqlx::MySqlPool;

use Server::master::{self, MoveGroupMoves, MoveGroups, Moves, Pachimon, StarterPartySlots};

#[tokio::main]
async fn main() {
    dotenvy::dotenv().ok();

    let database_url = std::env::var("DATABASE_URL").expect("DATABASE_URL must be set in .env");
    let pool = MySqlPool::connect(&database_url)
        .await
        .expect("Failed to connect to database");

    let move_groups = master::seed_move_groups_data();
    seed_move_groups(&pool, &move_groups).await;

    let moves = master::seed_moves_data();
    seed_moves(&pool, &moves).await;

    let move_group_moves = master::seed_move_group_moves_data();
    seed_move_group_moves(&pool, &move_group_moves).await;

    let pachimon = master::seed_pachimon_data();
    seed_pachimon(&pool, &pachimon).await;

    let starter_party_slots = master::seed_starter_party_slots_data();
    seed_starter_party_slots(&pool, &starter_party_slots).await;

    println!(
        "マスタデータの投入が完了しました(move_groups: {}件, moves: {}件, move_group_moves: {}件, pachimon: {}件, starter_party_slots: {}件)",
        move_groups.len(),
        moves.len(),
        move_group_moves.len(),
        pachimon.len(),
        starter_party_slots.len()
    );
}

async fn seed_move_groups(pool: &MySqlPool, move_groups: &[MoveGroups]) {
    for g in move_groups {
        sqlx::query(
            "INSERT INTO move_groups (move_group_id, name) VALUES (?, ?) \
             ON DUPLICATE KEY UPDATE name = VALUES(name)",
        )
        .bind(g.move_group_id)
        .bind(&g.name)
        .execute(pool)
        .await
        .unwrap_or_else(|err| {
            panic!(
                "move_group_id={}のシード投入に失敗しました: {err}",
                g.move_group_id
            )
        });
    }
}

async fn seed_moves(pool: &MySqlPool, moves: &[Moves]) {
    for m in moves {
        sqlx::query(
            "INSERT INTO moves \
                (move_id, name, move_type, category, base_power, accuracy, max_pp) \
             VALUES (?, ?, ?, ?, ?, ?, ?) \
             ON DUPLICATE KEY UPDATE \
                name = VALUES(name), \
                move_type = VALUES(move_type), \
                category = VALUES(category), \
                base_power = VALUES(base_power), \
                accuracy = VALUES(accuracy), \
                max_pp = VALUES(max_pp)",
        )
        .bind(m.move_id)
        .bind(&m.name)
        .bind(m.move_type as u8)
        .bind(m.category as u8)
        .bind(m.base_power)
        .bind(m.accuracy)
        .bind(m.max_pp)
        .execute(pool)
        .await
        .unwrap_or_else(|err| panic!("move_id={}のシード投入に失敗しました: {err}", m.move_id));
    }
}

async fn seed_move_group_moves(pool: &MySqlPool, rows: &[MoveGroupMoves]) {
    for r in rows {
        sqlx::query(
            "INSERT INTO move_group_moves (unique_id, group_id, move_id, is_initial) \
             VALUES (?, ?, ?, ?) \
             ON DUPLICATE KEY UPDATE \
                group_id = VALUES(group_id), \
                move_id = VALUES(move_id), \
                is_initial = VALUES(is_initial)",
        )
        .bind(r.unique_id)
        .bind(r.group_id)
        .bind(r.move_id)
        .bind(r.is_initial)
        .execute(pool)
        .await
        .unwrap_or_else(|err| panic!("unique_id={}のシード投入に失敗しました: {err}", r.unique_id));
    }
}

async fn seed_pachimon(pool: &MySqlPool, pachimon: &[Pachimon]) {
    for p in pachimon {
        sqlx::query(
            "INSERT INTO pachimon \
                (pachimon_id, name, primary_type, secondary_type, base_hp, base_atk, base_def, \
                 base_spatk, base_spdef, base_speed, rarity, move_group_id) \
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) \
             ON DUPLICATE KEY UPDATE \
                name = VALUES(name), \
                primary_type = VALUES(primary_type), \
                secondary_type = VALUES(secondary_type), \
                base_hp = VALUES(base_hp), \
                base_atk = VALUES(base_atk), \
                base_def = VALUES(base_def), \
                base_spatk = VALUES(base_spatk), \
                base_spdef = VALUES(base_spdef), \
                base_speed = VALUES(base_speed), \
                rarity = VALUES(rarity), \
                move_group_id = VALUES(move_group_id)",
        )
        .bind(p.pachimon_id)
        .bind(&p.name)
        .bind(p.primary_type as u8)
        .bind(p.secondary_type as u8)
        .bind(p.base_hp)
        .bind(p.base_atk)
        .bind(p.base_def)
        .bind(p.base_spatk)
        .bind(p.base_spdef)
        .bind(p.base_speed)
        .bind(p.rarity as u8)
        .bind(p.move_group_id)
        .execute(pool)
        .await
        .unwrap_or_else(|err| {
            panic!(
                "pachimon_id={}のシード投入に失敗しました: {err}",
                p.pachimon_id
            )
        });
    }
}

async fn seed_starter_party_slots(pool: &MySqlPool, slots: &[StarterPartySlots]) {
    for s in slots {
        sqlx::query(
            "INSERT INTO starter_party_slots (slot_no, pachimon_id) VALUES (?, ?) \
             ON DUPLICATE KEY UPDATE pachimon_id = VALUES(pachimon_id)",
        )
        .bind(s.slot_no)
        .bind(s.pachimon_id)
        .execute(pool)
        .await
        .unwrap_or_else(|err| panic!("slot_no={}のシード投入に失敗しました: {err}", s.slot_no));
    }
}
