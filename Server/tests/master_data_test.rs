// tests/master_data_test.rs
//
// MasterData::load (起動時にDBからマスタデータを読み込む処理) のテスト。
// - test_load_master_data_when_empty: pachimonテーブルが空の場合、空のVecで成功することを確認
// - test_load_master_data_with_seeded_rows: INSERT済みのpachimon行が、PachimonType/Rarityへの
//   デコードも含めて期待通りにロードされることを確認
// - test_load_starter_party_slots: INSERT済みのstarter_party_slots行がロードできることを確認
use sqlx::MySqlPool;

use Server::master::{MasterData, PachimonType, Rarity};

/// `pachimon`テーブルが空の状態で`load`すると、エラーにならず空の`Vec`が返ることを確認する。
#[sqlx::test]
async fn test_load_master_data_when_empty(pool: MySqlPool) {
    let master = MasterData::load(&pool).await.unwrap();
    assert!(master.pachimon.is_empty());
    assert!(master.starter_party_slots.is_empty());
    assert!(master.items.is_empty());
}

/// `pachimon`テーブルに投入済みの行が、数値からEnumへのデコードも含めて
/// 正しくロードされることを確認する。
#[sqlx::test]
async fn test_load_master_data_with_seeded_rows(pool: MySqlPool) {
    sqlx::query("INSERT INTO move_groups (move_group_id, name) VALUES (1, 'イフリーガ')")
        .execute(&pool)
        .await
        .unwrap();

    sqlx::query(
        "INSERT INTO pachimon \
            (pachimon_id, name, primary_type, secondary_type, base_hp, base_atk, base_def, \
             base_spatk, base_spdef, base_speed, rarity, move_group_id) \
         VALUES (1, 'イフリーガ', 2, 10, 78, 104, 78, 159, 115, 100, 1, 1)",
    )
    .execute(&pool)
    .await
    .unwrap();

    let master = MasterData::load(&pool).await.unwrap();
    assert_eq!(master.pachimon.len(), 1);

    let pachimon = &master.pachimon[0];
    assert_eq!(pachimon.pachimon_id, 1);
    assert_eq!(pachimon.name, "イフリーガ");
    assert_eq!(pachimon.primary_type, PachimonType::Fire);
    assert_eq!(pachimon.secondary_type, PachimonType::Flying);
    assert_eq!(pachimon.rarity, Rarity::S);
    assert_eq!(pachimon.base_hp, 78);
}

/// `starter_party_slots`テーブルに投入済みの行が正しくロードされることを確認する。
#[sqlx::test]
async fn test_load_starter_party_slots(pool: MySqlPool) {
    sqlx::query("INSERT INTO move_groups (move_group_id, name) VALUES (1, 'イフリーガ')")
        .execute(&pool)
        .await
        .unwrap();

    sqlx::query(
        "INSERT INTO pachimon \
            (pachimon_id, name, primary_type, secondary_type, base_hp, base_atk, base_def, \
             base_spatk, base_spdef, base_speed, rarity, move_group_id) \
         VALUES (1, 'イフリーガ', 2, 10, 78, 104, 78, 159, 115, 100, 1, 1)",
    )
    .execute(&pool)
    .await
    .unwrap();

    sqlx::query("INSERT INTO starter_party_slots (slot_no, pachimon_id) VALUES (1, 1)")
        .execute(&pool)
        .await
        .unwrap();

    let master = MasterData::load(&pool).await.unwrap();
    assert_eq!(master.starter_party_slots.len(), 1);
    assert_eq!(master.starter_party_slots[0].slot_no, 1);
    assert_eq!(master.starter_party_slots[0].pachimon_id, 1);
}

/// `items`テーブルに投入済みの行が正しくロードされることを確認する。
#[sqlx::test]
async fn test_load_items(pool: MySqlPool) {
    sqlx::query("INSERT INTO items (item_id, name) VALUES (1, 'ジェム')")
        .execute(&pool)
        .await
        .unwrap();

    let master = MasterData::load(&pool).await.unwrap();
    assert_eq!(master.items.len(), 1);
    assert_eq!(master.items[0].item_id, 1);
    assert_eq!(master.items[0].name, "ジェム");
}
