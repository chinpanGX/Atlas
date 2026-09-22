-- player_pachimon_movesに専用のULID主キーを追加する。
--
-- player_party_slots等、このプロジェクトの他テーブルは「割当自体を独立したエンティティとして
-- ULIDで一意に参照できる」方針を採っているが、player_pachimon_movesはそれ以前に作られたテーブルで
-- 複合UNIQUE(player_pachimon_id, slot)のみに依存していた。方針に合わせて専用PKを追加する
-- (開発初期のためデータ移行は行わず、テーブルを作り直す)。
DROP TABLE IF EXISTS player_pachimon_moves;

CREATE TABLE player_pachimon_moves (
    player_pachimon_move_id CHAR(26) PRIMARY KEY,
    player_pachimon_id CHAR(26) NOT NULL,
    slot INT NOT NULL,
    move_id BIGINT NOT NULL,
    FOREIGN KEY (player_pachimon_id) REFERENCES player_pachimon(player_pachimon_id),
    FOREIGN KEY (move_id) REFERENCES moves(move_id),
    UNIQUE (player_pachimon_id, slot)
);
