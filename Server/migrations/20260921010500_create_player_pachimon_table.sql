-- プレイヤー所持パチモンテーブル(Shared/docs/design/outgame.md参照)。
--
-- levelカラムは持たない。このゲームは経験値によるレベルアップを持たず、
-- 全パチモンは内部的に固定レベル50として扱う(Atlas.BattleCore.BattleConstants.FixedLevel、
-- Shared/docs/design/battle.md参照)。
CREATE TABLE player_pachimon (
    player_pachimon_id CHAR(26) PRIMARY KEY,
    player_id CHAR(26) NOT NULL,
    pachimon_id BIGINT NOT NULL,
    ivs JSON NOT NULL,
    party_slot INT NULL,
    effort_values JSON NOT NULL,
    obtained_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (player_id) REFERENCES players(player_id),
    FOREIGN KEY (pachimon_id) REFERENCES pachimon(pachimon_id),
    UNIQUE (player_id, party_slot)
);
