-- 技マスタテーブル。master-data-pipelineが生成するmaster_data/moves.jsonの内容を
-- `cargo run --bin seed_master_data`でUPSERTする。
--
-- move_type/categoryは、パイプラインが生成するPachimonType/MoveCategory Enumの
-- 数値表現をそのまま保存する(pachimon.primary_type等と同じ方針)。
CREATE TABLE moves (
    move_id BIGINT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    move_type TINYINT UNSIGNED NOT NULL,
    category TINYINT UNSIGNED NOT NULL,
    base_power BIGINT NOT NULL,
    accuracy BIGINT NOT NULL,
    max_pp BIGINT NOT NULL
);
