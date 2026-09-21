-- pachimon.move_group_idへの外部キー制約を追加する。
-- create_pachimon_table.sql作成時点ではmove_groupsテーブルが未実装だったため
-- 制約なしとしていたが、move_groupsテーブルを追加したのでここで解消する。
ALTER TABLE pachimon
    ADD FOREIGN KEY (move_group_id) REFERENCES move_groups(move_group_id);
