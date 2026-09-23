-- 対戦ログテーブル(Shared/docs/design/battle.md「DB設計」参照)。
--
-- 1ターン内の行動ごとに1行。action_data/result_dataはBattleServerから届いたJSONをそのまま保存する。
CREATE TABLE battle_turns (
    turn_id CHAR(26) PRIMARY KEY,
    match_id CHAR(26) NOT NULL,
    turn_number INT NOT NULL,
    player_id CHAR(26) NOT NULL,
    action_data JSON NOT NULL,
    result_data JSON NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (match_id) REFERENCES battle_matches(match_id),
    FOREIGN KEY (player_id) REFERENCES players(player_id)
);
