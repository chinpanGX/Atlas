-- アイテムマスタテーブル。master-data-pipelineが生成するmaster_data/items.jsonの内容を
-- `cargo run --bin seed_master_data`でUPSERTする(pachimonと同様の物理マスタテーブル)。
--
-- item_id=1はジェム(スカウトの紹介コストに使う課金通貨相当)。
CREATE TABLE items (
    item_id BIGINT PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);
