// マスタデータ投入コマンド。
//
// master-data-pipelineが生成したmaster_data/*.json(現状はpachimon.jsonのみ)を
// MySQLへUPSERTする。APIサーバー自体は起動時にDBからマスタを読み込みメモリキャッシュを
// 参照する設計のため、マスタ更新時はこのコマンドを実行してDBへ反映したのち、
// サーバーを再起動して反映する(詳細はShared/docs/design/architecture.mdの「マスターデータ運用」参照)。
//
// 実行方法: cargo run --bin seed_master_data
use sqlx::MySqlPool;

use Server::master::{self, Pachimon};

#[tokio::main]
async fn main() {
    dotenvy::dotenv().ok();

    let database_url = std::env::var("DATABASE_URL").expect("DATABASE_URL must be set in .env");
    let pool = MySqlPool::connect(&database_url)
        .await
        .expect("Failed to connect to database");

    let pachimon = master::seed_pachimon_data();
    seed_pachimon(&pool, &pachimon).await;

    println!("マスタデータの投入が完了しました(pachimon: {}件)", pachimon.len());
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
            panic!("pachimon_id={}のシード投入に失敗しました: {err}", p.pachimon_id)
        });
    }
}
