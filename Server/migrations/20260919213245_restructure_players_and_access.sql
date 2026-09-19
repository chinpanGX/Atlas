-- api-design.md改訂に伴うスキーマ変更:
-- player_idをdevicesから分離してplayersテーブルへ、access_tokensはdevice_idをPKに、
-- messagesはmessage_id(ULID)/player_id参照に変更する。

-- devicesに依存する既存テーブルを先にDROP(開発初期のためデータ移行は行わない)
DROP TABLE IF EXISTS messages;
DROP TABLE IF EXISTS access_tokens;

ALTER TABLE devices
    MODIFY COLUMN device_id CHAR(26) NOT NULL,
    MODIFY COLUMN created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    DROP COLUMN player_id;

CREATE TABLE players (
    player_id CHAR(26) PRIMARY KEY,
    device_id CHAR(26) NOT NULL UNIQUE,
    nickname VARCHAR(50) NOT NULL,
    gems INT NOT NULL DEFAULT 0,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);

CREATE TABLE access_tokens (
    device_id CHAR(26) PRIMARY KEY,
    access_token VARCHAR(64) NOT NULL UNIQUE,
    expires_at DATETIME(3) NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);

CREATE TABLE messages (
    message_id CHAR(26) PRIMARY KEY,
    player_id CHAR(26) NOT NULL,
    content TEXT NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (player_id) REFERENCES players(player_id)
);

CREATE INDEX idx_messages_created_at ON messages(created_at);
