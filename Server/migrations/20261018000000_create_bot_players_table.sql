-- 対戦相手のボットとして使うプレイヤー。
--
-- マッチングで一定時間相手が見つからなかったプレイヤーは、ここにいるプレイヤーとの対戦になる。
-- ボットもplayersの行を持つ普通のプレイヤーとして作る(battle_matchesの外部キーと、
-- BattleServerがパーティ・選出個体を取得する内部APIを、人との対戦と同じに使うため)。
-- デバイスは紐付けないので、クライアントからサインインされることは無い。
CREATE TABLE bot_players (
    player_id CHAR(26) PRIMARY KEY,
    FOREIGN KEY (player_id) REFERENCES players(player_id)
);
