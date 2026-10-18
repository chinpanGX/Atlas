---
title: "値なしの表し方(nullable)"
status: current
area: conventions
related: [conventions/player-diff, db/index, server/matchmaking]
proto: [Shared/proto/atlas/v1, Shared/proto/atlas/internal/v1/battle.proto]
updated: 2026-10-18
---

# 値なしの表し方(nullable)

| 経路 | nullable | 「値なし」の表し方 |
|---|---|---|
| gRPC(`.proto`。クライアント向け・内部 API) | スカラーは `optional` を使わない | 取り決めた番兵値(空文字など)。メッセージ型のフィールドは `null` を「変更なし / 該当なし」として使ってよい |
| DB | `NULL` を許す列はなるべく作らない | 行が無いこと |
| MagicOnion(MessagePack。Client ⇔ BattleServer) | 使ってよい | |

## 例

- パーティの未編成の枠は `player_party_slots` に行を作らない
- マッチングが成立していないときの `GetQueueStatus` の `match_id`・`battle_server`・`battle_token` は空文字
  ([マッチング](../server/matchmaking.md))
- 勝者なしの対戦の結果報告の `winner_id`、一度も参加しなかった側の `player1_id`/`player2_id` は空文字([内部API](../server/internal-api.md))
- `player_diff` の種別ごとのフィールドは、変化が無ければ `null`([所持データの差分](player-diff.md))
- マスタでは、2つ目のタイプが無いパチモンの `secondary_type` を `NONE`、変化技の `base_power` を0にする
  (マスターデータのパイプラインが nullable を持てないため)
- 状態によって中身が決まる列だけ、例外として DB で `NULL` を許す(`scout_rolls` の `selected_index`・`selected_at`、`battle_matches` の選出・勝者・日時)

## 理由

- gRPC は基盤(VolTech gRPC)の規約に合わせた
- 「無い」を行が無いことで表すと、API に nullable を出さずに済み、割当を独立したテーブルにする設計([DB](../db/index.md))とも合う
