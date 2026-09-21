-- 技グループマスタテーブル。master-data-pipelineが生成するmaster_data/move_groups.jsonの
-- 内容を`cargo run --bin seed_master_data`でUPSERTする。
CREATE TABLE move_groups (
    move_group_id BIGINT PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);
