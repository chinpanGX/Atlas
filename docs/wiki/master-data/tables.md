---
title: "マスターデータのテーブル"
status: current
area: master-data
related: [master-data/pipeline, game/pachimon, battle-server/battle-core]
code: [Shared/master-data/schema, Shared/master-data/csv]
updated: 2026-10-12
---

# マスターデータのテーブル

本家ポケモンの対戦から、天候・フィールド・持ち物・特性・技の追加効果を外した構成。レベルアップで技を覚える仕組みは無く、
パチモンに「技グループ」を紐付ける。

## pachimon(136件)

| 列 | 説明 |
|---|---|
| `pachimon_id` | PK。4桁(1001〜) |
| `name` | |
| `primary_type` / `secondary_type` | `pachimon_type`。2つ目が無い場合は `NONE` |
| `base_hp` / `base_atk` / `base_def` / `base_spatk` / `base_spdef` / `base_speed` | 種族値 |
| `rarity` | `rarity`(S/A/B/C)。スカウトの抽選に使う。合計種族値で決める(580以上 S・520以上 A・460以上 B・それ未満 C) |
| `move_group_id` | FK → `move_groups` |

## move_groups

| 列 | 説明 |
|---|---|
| `move_group_id` | PK |
| `name` | 管理用の名前 |

複数のパチモンで共有できる設計だが、現状のデータは1パチモン1グループ(`pachimon_id` と同じ値)。

## move_group_moves(技グループが含む技。各グループ20〜48件)

| 列 | 説明 |
|---|---|
| `unique_id` | PK。パイプラインが複合主キーに対応しないための代理キー |
| `group_id` | FK → `move_groups` |
| `move_id` | FK → `moves` |
| `is_initial` | 初期技か。各グループちょうど4件(APIサーバーのテストで確かめている) |

## moves(261件)

| 列 | 説明 |
|---|---|
| `move_id` | PK |
| `name` | |
| `move_type` | `pachimon_type`(Rust の予約語 `type` を避けた列名) |
| `category` | `move_category`(physical/special/status) |
| `base_power` | 威力。変化技は0 |
| `accuracy` | 命中率 |
| `max_pp` | |

変化技は定義できるが、追加効果が無いため現状は効果なし。初期技は物理・特殊技だけにする。

## type_chart(324件 = 18タイプ × 18タイプ)

| 列 | 説明 |
|---|---|
| `type_chart_id` | PK。`UNIQUE(attack_type, defend_type)` の代わりの代理キー |
| `attack_type` / `defend_type` | `pachimon_type` |
| `effectiveness` | `type_effectiveness`(IMMUNE / NOT_VERY_EFFECTIVE / NORMAL / SUPER_EFFECTIVE) |

- 区分 → 倍率(0/0.5/1/2)の変換と、2タイプのかけ合わせは BattleCore に書く
- 「行が無ければ1倍」という暗黙の既定値は作らず、全タイプ × 全タイプの組み合わせを CSV に書く
- DB には入れない

## starter_party_slots

| 列 | 説明 |
|---|---|
| `slot_no` | PK(1〜6) |
| `pachimon_id` | FK → `pachimon` |

新規プレイヤーに複製する固定の6体(現状はレア度 C の6体)。

## items

| 列 | 説明 |
|---|---|
| `item_id` | PK。`1` がジェム、`2` が育成チケット |
| `name` | |

## shop_products(追加予定の機能のマスタ)

| 列 | 説明 |
|---|---|
| `shop_product_id` | PK |
| `item_id` | FK → `items`。購入で受け取るアイテム |
| `amount` | 1回の購入で受け取る数 |
| `cost_item_id` | FK → `items`。支払うアイテム(現状はジェム) |
| `cost_amount` | 1回の購入で支払う数 |

- 現状は3件(育成チケット10枚 = ジェム50、100枚 = 450、200枚 = 800)
- マスタにある商品はいつでも買える(販売期間を持たない)。パイプラインに日時の型が無いこともあり、期間を持つスカウトのバナーとは違ってマスタで持つ
- DB のテーブル(マイグレーション)と `seed_master_data` への投入は未作成

## Enum

`Shared/master-data/schema/enums/` の `pachimon_type`(18タイプ + `NONE`)、`rarity`、`move_category`、`type_effectiveness`。
