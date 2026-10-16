---
title: "スカウトのテーブル"
status: current
area: db
related: [db/index, server/scout, game/scout]
code: [Server/migrations, Server/src/master/scout_banner.rs, Server/src/model/scout_roll.rs]
updated: 2026-10-16
---

# スカウトのテーブル

## scout_banners

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `banner_id` | CHAR(26) | PK | |
| `name` | VARCHAR(100) | NOT NULL | |
| `cost_per_roll` | INT | NOT NULL | 紹介1回のジェム |
| `start_at` / `end_at` | DATETIME(3) | NOT NULL | 開催期間(UTC) |

マスターデータ(`seed_master_data` で投入する)。マスタの CSV では日時を UTC の文字列で持ち、投入するときに変換する
([マスターデータのテーブル](../master-data/tables.md))。

## scout_banner_rates

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `scout_banner_rate_id` | INT | PK | 代理キー(パイプラインが複合主キーを持てないため) |
| `banner_id` | CHAR(26) | NOT NULL, FK → `scout_banners` | |
| `rarity` | TINYINT UNSIGNED | NOT NULL | `Rarity` の値 |
| `weight` | INT | NOT NULL | 1万分率の重み(3% = 300)。バナーごとの合計は10000 |

マスターデータ。`seed_master_data` は、マスタから消した行が残って合計が崩れないように、全件を消してから入れ直す。

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
