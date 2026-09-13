-- Add migration script here
CREATE TABLE messages(
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    sender_player_id VARCHAR(26) NOT NULL,
    content TEXT NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (sender_player_id) REFERENCES devices(player_id)
);

CREATE INDEX  idx_messages_created_at ON messages(created_at);