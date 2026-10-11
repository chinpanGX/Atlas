-- プレイヤー(ゲームデータの持ち主)。player_idはgRPC基盤のデバイス登録で発行したULID。
-- デバイスとの対応は基盤のdevicesテーブルだけが持つので、ここにはdevice_idを持たない
CREATE TABLE players (
    player_id CHAR(26) PRIMARY KEY,
    nickname VARCHAR(50) NOT NULL,
    gems INT NOT NULL DEFAULT 0,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
);

CREATE TABLE messages (
    message_id CHAR(26) PRIMARY KEY,
    player_id CHAR(26) NOT NULL,
    content TEXT NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (player_id) REFERENCES players(player_id)
);

CREATE INDEX idx_messages_created_at ON messages(created_at);
