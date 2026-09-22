-- スターター編成マスタテーブル(Shared/docs/design/outgame.md参照)。
--
-- master-data-pipelineが生成するmaster_data/starter_party_slots.jsonの内容を
-- `cargo run --bin seed_master_data`でUPSERTする。新規プレイヤー作成時にこの内容を
-- player_pachimon/player_party_slotsへ複製し、初期パーティとして自動付与する
-- (`src/service/player_service.rs`参照)。
CREATE TABLE starter_party_slots (
    slot_no BIGINT PRIMARY KEY,
    pachimon_id BIGINT NOT NULL,
    FOREIGN KEY (pachimon_id) REFERENCES pachimon(pachimon_id)
);
