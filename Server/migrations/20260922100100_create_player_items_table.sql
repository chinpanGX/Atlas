-- プレイヤー所持アイテムテーブル(Shared/docs/design/outgame.md参照)。
--
-- 所持数0のアイテムは行自体を持たない(nullableを避け、「未所持」を行の不在で表現する)。
CREATE TABLE player_items (
    player_id CHAR(26) NOT NULL,
    item_id BIGINT NOT NULL,
    quantity INT NOT NULL,
    updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    PRIMARY KEY (player_id, item_id),
    FOREIGN KEY (player_id) REFERENCES players(player_id),
    FOREIGN KEY (item_id) REFERENCES items(item_id)
);
