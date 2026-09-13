-- Add migration script here
CREATE TABLE access_tokens (
    token VARCHAR(64) PRIMARY KEY,
    device_id VARCHAR(26) NOT NULL UNIQUE,
    expires_at DATETIME NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);