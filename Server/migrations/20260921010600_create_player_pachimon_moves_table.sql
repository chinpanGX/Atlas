-- プレイヤー所持パチモンが現在覚えている技(Shared/docs/design/outgame.md参照)。
--
-- スカウトで個体が生成される際、対応するmove_group_masterのis_initial = TRUEの行を
-- そのまま複製して初期セットする(詳細はShared/docs/design/scout.md参照)。技の付け替えは
-- このテーブルの対象slotをUPDATEするだけで実現でき、マスタ側の変更は不要。
CREATE TABLE player_pachimon_moves (
    player_pachimon_id CHAR(26) NOT NULL,
    slot INT NOT NULL,
    move_id BIGINT NOT NULL,
    FOREIGN KEY (player_pachimon_id) REFERENCES player_pachimon(player_pachimon_id),
    FOREIGN KEY (move_id) REFERENCES moves(move_id),
    UNIQUE (player_pachimon_id, slot)
);
