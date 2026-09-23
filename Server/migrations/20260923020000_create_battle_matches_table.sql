-- 対戦テーブル(Shared/docs/design/battle.md「DB設計」参照)。
--
-- マッチ成立時(POST /battle/queue)にstatus='in_progress'で作成し、
-- BattleServerからの結果報告(POST /internal/battle/result)でstatus='finished'に更新する。
-- player1_id/player2_idはマッチ成立時の順番(先に待っていた側がplayer1)。
CREATE TABLE battle_matches (
    match_id CHAR(26) PRIMARY KEY,
    player1_id CHAR(26) NOT NULL,
    player2_id CHAR(26) NOT NULL,
    status ENUM('matching', 'in_progress', 'finished') NOT NULL,
    winner_id CHAR(26) NULL,
    player1_selected_pachimon JSON NULL,
    player2_selected_pachimon JSON NULL,
    started_at DATETIME(3) NULL,
    ended_at DATETIME(3) NULL,
    FOREIGN KEY (player1_id) REFERENCES players(player_id),
    FOREIGN KEY (player2_id) REFERENCES players(player_id),
    FOREIGN KEY (winner_id) REFERENCES players(player_id)
);
