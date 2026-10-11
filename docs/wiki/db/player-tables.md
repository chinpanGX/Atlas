---
title: "プレイヤーと所持データのテーブル"
status: current
area: db
related: [db/index, server/player, server/party, conventions/player-diff, conventions/items]
code: [Server/migrations, Server/src/model]
updated: 2026-10-11
---

# プレイヤーと所持データのテーブル

## players

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_id` | CHAR(26) | PK | デバイス登録で基盤が発行した ULID |
| `nickname` | VARCHAR(50) | NOT NULL | |
| `created_at` | DATETIME(3) | NOT NULL | |

ジェム等の数は持たない(`player_items` で持つ)。デバイスとの対応は基盤の `devices` だけが持つので、`device_id` は持たない。

## player_items

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_id` | CHAR(26) | PK(複合), FK → `players` | |
| `item_id` | INT | PK(複合), FK → `items` | `1` がジェム |
| `quantity` | INT | NOT NULL | 所持数(絶対値) |
| `updated_at` | DATETIME(3) | NOT NULL | |

個々の行を独立したエンティティとして指す必要が無いため、ULID ではなく `(player_id, item_id)` の複合主キーにしている。
増減の仕方は [所持リソース](../conventions/items.md)。

## player_pachimon

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_pachimon_id` | CHAR(26) | PK | |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | |
| `pachimon_id` | INT | NOT NULL, FK → `pachimon` | 種族 |
| `effort_values` | JSON | NOT NULL | 能力に割り振ったポイント。`{"hp","atk","def","spatk","spdef","speed"}`、入手時はすべて0 |
| `obtained_at` | DATETIME(3) | NOT NULL | |

- レベルは列を持たない(全員50の固定値)。個体値も持たない
- `effort_values` はステータスポイント(64ポイント)の入れ物。割り振る機能は追加予定で、計算式への反映の仕方も未確定

## player_pachimon_moves

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_pachimon_move_id` | CHAR(26) | PK | 割当の ID。付け替えでは変わらない |
| `player_pachimon_id` | CHAR(26) | NOT NULL, FK → `player_pachimon` | |
| `slot` | INT | NOT NULL | 1〜4。`UNIQUE(player_pachimon_id, slot)` |
| `move_id` | INT | NOT NULL, FK → `moves` | |

個体を作るとき(スターター・スカウト)に、技グループの初期技4つを複製する。

## player_party_slots

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `party_slot_id` | CHAR(26) | PK | 割当の ID。編成し直すたびに作り直す |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | |
| `slot` | INT | NOT NULL | 1〜6。`UNIQUE(player_id, slot)` |
| `player_pachimon_id` | CHAR(26) | NOT NULL, FK → `player_pachimon` | `UNIQUE(player_id, player_pachimon_id)`(同じ個体を2つの枠に入れない) |
| `created_at` | DATETIME(3) | NOT NULL | |

## 採用しなかった案

- `player_pachimon` に `party_slot`(NULL 可)の列を持たせる: 未編成を `NULL` で表すことになり、API に nullable が出てくるため
