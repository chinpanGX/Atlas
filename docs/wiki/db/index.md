---
title: "DB"
status: current
area: db
related: [conventions/naming-and-ids, conventions/nullable, master-data/tables]
code: [Server/migrations, Server/src/db.rs]
updated: 2026-10-16
---

# DB

MySQL。唯一のデータストアで、書き込むのは APIサーバーだけ。

## 方針

- スキーマは `Server/migrations/` のマイグレーション(`sqlx migrate`)で管理する。gRPC 基盤のテーブルと合わせて、APIサーバーの起動時に流す
- プレイヤーの操作で生成する行の主キーは ULID(`CHAR(26)`)。日時は `DATETIME(3)`
- `NULL` を許す列はなるべく作らない。「無い」ことは行が無いことで表す([値なしの表し方](../conventions/nullable.md))
- 所持データの「割当」(パーティの枠・覚えている技)は、元のテーブルの列ではなく専用のテーブルにする。未設定を行が無いことで
  表せ、nullable を使わずに済むため
- マスタのテーブル(`pachimon` 等)も DB に置く。定義は [マスターデータのテーブル](../master-data/tables.md)
- マッチングの待機列と対戦の進行中の状態は DB に置かない(サーバーのメモリ)

## テーブル一覧

| テーブル | 内容 | 主な書き込み元 | ページ |
|---|---|---|---|
| `devices` | 登録されたデバイス(基盤) | `DeviceService/Register` | [プレイヤーと所持データ](player-tables.md) |
| `access_tokens` | デバイスごとのアクセストークン(基盤。1デバイス1行) | `DeviceService/IssueToken` | 同上 |
| `last_responses` | 冪等性の対象の RPC の直前のレスポンス(基盤。1プレイヤー1行) | 冪等性の対象の RPC・サインアップ | 同上 |
| `game_logs` | ゲームログ(所持データの変化の履歴。基盤。[ゲームログ](../server/game-log.md)) | 所持アイテムを変える RPC | 同上 |
| `players` | プレイヤー | `SignUp` | 同上 |
| `player_items` | 所持アイテム(ジェム等)の数 | サインアップ・スカウト・対戦の結果・開発用 | 同上 |
| `player_pachimon` | 所持しているパチモンの個体 | サインアップ・スカウト・開発用 | 同上 |
| `player_pachimon_moves` | 個体が覚えている技(1個体4行まで) | サインアップ・スカウト・技の付け替え・開発用 | 同上 |
| `player_party_slots` | パーティの編成(1プレイヤー6行まで) | サインアップ・パーティ編成・開発用 | 同上 |
| `scout_banners` | スカウトのバナー(値段・期間) | `seed_master_data` | [スカウト](scout-tables.md) |
| `scout_banner_rates` | スカウトのバナーのレア度ごとの排出率 | `seed_master_data` | [スカウト](scout-tables.md) |
| `scout_rolls` | スカウトの紹介と選択の記録 | `Roll`・`Select` | 同上 |
| `battle_matches` | 対戦 | マッチ成立・結果報告・後始末 | [対戦](battle-tables.md) |
| `battle_turns` | 対戦の行動ログ | 結果報告 | 同上 |
| `pachimon`・`moves`・`move_groups`・`move_group_moves`・`starter_party_slots`・`items` | マスタ | `seed_master_data` | [マスターデータのテーブル](../master-data/tables.md) |

## 基盤のテーブル

`devices`・`access_tokens`・`last_responses` は gRPC 基盤のクレートのマイグレーションが作る(履歴は `_auth_migrations`・
`_transaction_migrations`。Atlas の `_sqlx_migrations` とは別)。列は基盤が決めるので、この wiki には書かない。
基盤のテーブルの `player_id` は `BINARY(16)` で、Atlas のテーブルの `CHAR(26)` とはつなぎ目で変換する。

## マイグレーションの注意

適用済みのマイグレーションの SQL を書き換えると、ローカル DB の `_sqlx_migrations` のチェックサムと合わなくなる。書き換えた場合は、
ファイルの SHA-384 でチェックサムを更新するか、DB を作り直す(`server-dev-env` スキル)。
