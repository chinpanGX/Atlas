-- スカウトの紹介記録(Shared/docs/design/scout.md参照)。
--
-- candidatesに選択結果も含めて記録するため、紹介履歴と入手履歴を兼ねる。
CREATE TABLE scout_rolls (
    roll_id CHAR(26) PRIMARY KEY,
    player_id CHAR(26) NOT NULL,
    banner_id CHAR(26) NOT NULL,
    candidates JSON NOT NULL,
    selected_index INT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    selected_at DATETIME(3) NULL,
    FOREIGN KEY (player_id) REFERENCES players(player_id),
    FOREIGN KEY (banner_id) REFERENCES scout_banners(banner_id)
);
