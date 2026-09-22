-- プレイヤーのgems所持数はplayer_items(item_id=1)へ一本化したため、players.gems列を廃止する。
ALTER TABLE players
    DROP COLUMN gems;
