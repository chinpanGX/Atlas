---
title: "スカウトのテーブル"
status: current
area: db
related: [db/index, server/scout, game/scout]
code: [Server/migrations, Server/src/model/scout_banner.rs, Server/src/model/scout_roll.rs]
updated: 2026-10-11
---

# スカウトのテーブル

## scout_banners

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `banner_id` | CHAR(26) | PK | |
| `name` | VARCHAR(100) | NOT NULL | |
| `rate_table` | JSON | NOT NULL | レア度ごとの確率(合計1.0)。例: `{"S":0.03,"A":0.12,"B":0.35,"C":0.50}` |
| `cost_per_roll` | INT | NOT NULL | 紹介1回のジェム |
| `start_at` / `end_at` | DATETIME(3) | NOT NULL | 開催期間 |

マスターデータではなく、`seed_scout_banners` で投入する([スカウト(API)](../server/scout.md) の「設計上の判断」)。

## scout_rolls

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `roll_id` | CHAR(26) | PK | |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | |
| `banner_id` | CHAR(26) | NOT NULL, FK → `scout_banners` | |
| `candidates` | JSON | NOT NULL | 候補10体。各要素は `{"pachimonId","rarity","moves"}` |
| `selected_index` | INT | NULL 可 | 選んだ候補(0〜9)。未選択は `NULL` |
| `created_at` | DATETIME(3) | NOT NULL | 紹介を受けた日時(ジェムを消費した日時) |
| `selected_at` | DATETIME(3) | NULL 可 | 選んだ日時 |

候補と選択結果をまとめて記録し、紹介と入手の履歴を兼ねる。
