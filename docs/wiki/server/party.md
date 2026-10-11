---
title: "パーティ編成・技の付け替え"
status: current
area: server
related: [game/party-training, db/player-tables, conventions/player-diff]
proto: [Shared/proto/atlas/v1/party.proto]
code: [Server/src/grpc/party.rs, Server/src/service/player_pachimon_service.rs]
updated: 2026-10-11
---

# パーティ編成・技の付け替え

## PartyService/SetParty

`party_slots`(`slot`・`player_pachimon_id` の組) → `player_diff`

- 全置き換え。既存の枠をすべて削除し、指定された枠を新しい ULID で作り直す(リクエストに無い枠は解除される)
- 検証(どれかに当たれば何も変えない)
  1. 1〜6体、枠は1〜6で重複しない、同じ個体を2つの枠に入れない → `INVALID_PARTY`
  2. すべて自分の所持個体であること → `PACHIMON_NOT_OWNED`
- 同じ種族の別の個体は何体でもよい
- `player_diff` の `party_slots` は、以前の割当がすべて `removed`、新しい割当が `upserted`
- 割当の ID を作り直すので、2回実行すると結果が変わる。冪等性の対象にし、再送では二重に実行しない

## PartyService/SetPachimonMove

`player_pachimon_id`・`slot`・`move_id` → `player_diff`

- 技を、その種族の技グループの候補(`move_group_moves`)から選んで入れ替える。何度でも付け替えられる(育成チケットの消費は追加予定)
- 検証の順: 枠が1〜4でない → `INVALID_MOVE_SLOT`、自分の所持個体でない → `PACHIMON_NOT_OWNED`、候補に無い技 → `MOVE_NOT_LEARNABLE`
- `INSERT ... ON DUPLICATE KEY UPDATE move_id = VALUES(move_id)` で更新する。更新の対象に `player_pachimon_move_id` を含めないため、
  既にある枠の付け替えでは割当の ID が変わらない
- `player_diff` の `pachimon_move_map` に付け替えた1件が入る
- クライアントには呼び出す処理(`IPachimonMoveEditService`)だけがあり、画面は無い
