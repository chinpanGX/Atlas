-- パチモンマスタテーブル。master-data-pipelineが生成するmaster_data/pachimon.jsonの内容を
-- `cargo run --bin seed_master_data`でUPSERTする。
--
-- pachimon_idはスプレッドシート側で採番されるため、AUTO_INCREMENTにはしない。
-- primary_type/secondary_type/rarityは、パイプラインが生成するPachimonType/Rarity Enumの
-- 数値表現(BIGINT)をそのまま保存する(secondary_typeが無い個体は0=PachimonType::None)。
-- move_group_idはmove_groupsテーブルが未実装のため、現時点では外部キー制約を付けない。
CREATE TABLE pachimon (
    pachimon_id BIGINT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    primary_type BIGINT NOT NULL,
    secondary_type BIGINT NOT NULL,
    base_hp BIGINT NOT NULL,
    base_atk BIGINT NOT NULL,
    base_def BIGINT NOT NULL,
    base_spatk BIGINT NOT NULL,
    base_spdef BIGINT NOT NULL,
    base_speed BIGINT NOT NULL,
    rarity BIGINT NOT NULL,
    move_group_id BIGINT NOT NULL
);
