---
title: "パーティ編成・育成"
status: current
area: server
related: [game/party-training, db/player-tables, conventions/player-diff, conventions/items, server/game-log]
proto: [Shared/proto/atlas/v1/party.proto]
code: [Server/src/grpc/party.rs, Server/src/service/player_pachimon_service.rs, Server/src/model/player_pachimon.rs]
updated: 2026-10-17
---

# パーティ編成・育成

育成(技の付け替え・ステータスポイントの割り振り)には育成チケット(`item_id: 2`)を使う。チケットはショップで買う([ショップ](shop.md))。

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

- 技を、その種族の技グループの候補(`move_group_moves`。初期技も含む)から選んで入れ替え、育成チケットを5枚使う
- 検証の順(どれかに当たればチケットを使わず、何も変えない)
  1. 枠が1〜4でない → `INVALID_MOVE_SLOT`
  2. 自分の所持個体でない → `PACHIMON_NOT_OWNED`
  3. 候補に無い技 → `MOVE_NOT_LEARNABLE`
  4. その枠に今と同じ技が入っている → `MOVE_UNCHANGED`
  5. 別の枠でその技を覚えている → `MOVE_ALREADY_LEARNED`
  6. チケットが5枚に足りない → `INSUFFICIENT_TRAINING_TICKETS`
- 4・5を業務エラーにするのは、5枚使う操作で意味の無い付け替えをしてチケットを失わないようにするため
- 同じ個体を同時に付け替えて同じ技が2つの枠に入らないよう、`player_pachimon` の行を `FOR UPDATE` でロックしてから覚えている技を確かめる
- `INSERT ... AS new ON DUPLICATE KEY UPDATE move_id = new.move_id` で更新する。更新の対象に `player_pachimon_move_id` を含めないため、
  既にある枠の付け替えでは割当の ID が変わらない
- `player_diff` の `pachimon_move_map` に付け替えた1件、`items` に使った後のチケットが入る
- クライアントには呼び出す処理(`IPachimonMoveEditService`)だけがあり、画面は無い

## PartyService/SetPachimonStatPoints

`player_pachimon_id`・`stat_points`(6能力の割り振りの後の値) → `player_diff`

- 割り振りの後の値を受け取る(増減ではなく)。64を使い切らなくてよく、1つの能力の上限は無い
- 使うチケットは、増やしたポイントの合計(能力ごとの `max(0, 新しい値 - 今の値)` の和)。減らした分は差し引かない(減らすのは無料のため)
- 検証の順(どれかに当たれば何も変えない)
  1. 負の値がある・合計が64を超える・`stat_points` が無い → `INVALID_STAT_POINTS`
  2. 自分の所持個体でない → `PACHIMON_NOT_OWNED`
  3. チケットが足りない → `INSUFFICIENT_TRAINING_TICKETS`
- 今と同じ割り振りを送ったときは、チケットを使わず何も変えない(`player_diff` は無く、ゲームログも残らない)
- 同時に割り振ったときに、両方が同じ「今の値」から枚数を数えないよう、`player_pachimon` の行を `FOR UPDATE` でロックしてから今の値を読む
- `player_pachimon.effort_values` に保存する。`player_diff` の `pachimon` に変えた1体(`stat_points` 込み)、
  チケットを使ったときは `items` に使った後のチケットが入る
- 所持個体の割り振りは `PlayerPachimon.stat_points` として、サインインの全件と差分の両方で返す
