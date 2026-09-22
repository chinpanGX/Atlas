-- パーティ編成テーブル(Shared/docs/design/outgame.md参照)。
--
-- パーティへの割当自体を、player_pachimonのparty_slot(nullable INT)属性としてではなく、
-- 独立したエンティティ(ULIDを持つ行)として表現する。API応答でnullableを避けられる
-- (割当が無いslotは行自体が存在しない)ほか、他テーブルと同様に全ての永続データが
-- ULIDで一意に参照できるようになる。
CREATE TABLE player_party_slots (
    party_slot_id CHAR(26) PRIMARY KEY,
    player_id CHAR(26) NOT NULL,
    slot INT NOT NULL,
    player_pachimon_id CHAR(26) NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (player_id) REFERENCES players(player_id),
    FOREIGN KEY (player_pachimon_id) REFERENCES player_pachimon(player_pachimon_id),
    UNIQUE (player_id, slot),
    UNIQUE (player_id, player_pachimon_id)
);

-- 旧`UNIQUE(player_id, party_slot)`インデックス(`player_id`)はplayersへの外部キー制約が
-- 参照しているため、先に単独の`player_id`インデックスを追加してから旧インデックスを削除する。
ALTER TABLE player_pachimon
    ADD INDEX idx_player_pachimon_player_id (player_id),
    DROP INDEX player_id,
    DROP COLUMN party_slot;
