-- master-data-pipelineの type: int は本来32bit(C#のint/Rustのi32相当)の想定だったが、
-- server_codegenがRust側をi64で生成していたため、DB列もBIGINTで作成されていた
-- (C#側(int)とRust/DB側(i64/BIGINT)でビット幅が食い違うバグ)。
-- type: int を32bit、新設した type: long を64bit と明確に分離したため、
-- 既存の type: int 列をすべてBIGINT→INTへ縮小する(現時点で type: long を使う列は無い)。
--
-- FKで参照されている列(move_groups.move_group_id / moves.move_id / pachimon.pachimon_id /
-- items.item_id)を含むため、いったんFK制約を外してから型変更し、最後に張り直す。

-- 1. FK制約を一時的に外す
ALTER TABLE move_group_moves DROP FOREIGN KEY move_group_moves_ibfk_1;
ALTER TABLE move_group_moves DROP FOREIGN KEY move_group_moves_ibfk_2;
ALTER TABLE pachimon DROP FOREIGN KEY pachimon_ibfk_1;
ALTER TABLE player_items DROP FOREIGN KEY player_items_ibfk_2;
ALTER TABLE player_pachimon DROP FOREIGN KEY player_pachimon_ibfk_2;
ALTER TABLE player_pachimon_moves DROP FOREIGN KEY player_pachimon_moves_ibfk_2;
ALTER TABLE starter_party_slots DROP FOREIGN KEY starter_party_slots_ibfk_1;

-- 2. type: int 列をBIGINT→INTへ縮小
ALTER TABLE move_groups
    MODIFY COLUMN move_group_id INT NOT NULL;

ALTER TABLE moves
    MODIFY COLUMN move_id INT NOT NULL,
    MODIFY COLUMN base_power INT NOT NULL,
    MODIFY COLUMN accuracy INT NOT NULL,
    MODIFY COLUMN max_pp INT NOT NULL;

ALTER TABLE move_group_moves
    MODIFY COLUMN unique_id INT NOT NULL,
    MODIFY COLUMN group_id INT NOT NULL,
    MODIFY COLUMN move_id INT NOT NULL;

ALTER TABLE pachimon
    MODIFY COLUMN pachimon_id INT NOT NULL,
    MODIFY COLUMN base_hp INT NOT NULL,
    MODIFY COLUMN base_atk INT NOT NULL,
    MODIFY COLUMN base_def INT NOT NULL,
    MODIFY COLUMN base_spatk INT NOT NULL,
    MODIFY COLUMN base_spdef INT NOT NULL,
    MODIFY COLUMN base_speed INT NOT NULL,
    MODIFY COLUMN move_group_id INT NOT NULL;

ALTER TABLE items
    MODIFY COLUMN item_id INT NOT NULL;

ALTER TABLE starter_party_slots
    MODIFY COLUMN slot_no INT NOT NULL,
    MODIFY COLUMN pachimon_id INT NOT NULL;

ALTER TABLE player_items
    MODIFY COLUMN item_id INT NOT NULL;

ALTER TABLE player_pachimon
    MODIFY COLUMN pachimon_id INT NOT NULL;

ALTER TABLE player_pachimon_moves
    MODIFY COLUMN move_id INT NOT NULL;

-- 3. FK制約を張り直す
ALTER TABLE move_group_moves
    ADD FOREIGN KEY (group_id) REFERENCES move_groups(move_group_id),
    ADD FOREIGN KEY (move_id) REFERENCES moves(move_id);

ALTER TABLE pachimon
    ADD FOREIGN KEY (move_group_id) REFERENCES move_groups(move_group_id);

ALTER TABLE player_items
    ADD FOREIGN KEY (item_id) REFERENCES items(item_id);

ALTER TABLE player_pachimon
    ADD FOREIGN KEY (pachimon_id) REFERENCES pachimon(pachimon_id);

ALTER TABLE player_pachimon_moves
    ADD FOREIGN KEY (move_id) REFERENCES moves(move_id);

ALTER TABLE starter_party_slots
    ADD FOREIGN KEY (pachimon_id) REFERENCES pachimon(pachimon_id);
