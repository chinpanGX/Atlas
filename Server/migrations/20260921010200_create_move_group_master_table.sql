-- 技グループが含む技の対応表。master-data-pipelineが生成する
-- master_data/move_group_master.jsonの内容を`cargo run --bin seed_master_data`でUPSERTする。
--
-- unique_id: パイプラインは複合PK/複合UNIQUEに対応していないため、
-- UNIQUE(group_id, move_id)相当の制約はこの行の代理キー(unique_id)で代替している。
--
-- is_initial: スカウトで個体が生成される際、この値がTRUEの技をそのまま初期習得技として
-- 複製する(Shared/docs/design/scout.md参照)。
CREATE TABLE move_group_master (
    unique_id BIGINT PRIMARY KEY,
    group_id BIGINT NOT NULL,
    move_id BIGINT NOT NULL,
    is_initial BOOLEAN NOT NULL,
    FOREIGN KEY (group_id) REFERENCES move_groups(move_group_id),
    FOREIGN KEY (move_id) REFERENCES moves(move_id)
);
