---
name: master-data-schema-add
description: マスターデータに新しいテーブルやEnumを追加する、または既存テーブルにカラムを追加するときに使う。Shared/master-data配下のスキーマ定義から、生成・DB投入・ドキュメント更新までの一連の流れを漏れなく行うためのスキル。
---

# マスターデータ テーブル/Enum 追加フロー

正本は `Shared/master-data/`(Atlasリポジトリ側)。`master-data-pipeline`(別リポジトリ)は
汎用ツールなので、スキーマ定義そのものをそちらにコミットしない。

## 1. スキーマ定義

- テーブル: `Shared/master-data/schema/tables/<name>.yaml`
- Enum: `Shared/master-data/schema/enums/<enum_name>.yaml`

既存の `pachimon.yaml` / `pachimon_type.yaml` を参考にする。ポイント:

- テーブルの `fields[].name` はそのまま生成コードのフィールド名になる。Rust予約語を避ける
  (詳細は `master-data-pipeline` スキール参照。例: `type` → `move_type`)
- `fields[].targets` は省略時 `[client, server]`。サーバー内部専用の列(説明文など)は
  `targets: [server]` を明示する
- nullable型は無いので「値なし」はセンチネルで表現する
  (Enumなら `id: 0, key: None, name: NONE` のようなメンバーを用意する、int列なら意味のある既定値を割り当てる)
- 複合PK/複合UNIQUEが必要な場合は代理キー列(例: `unique_id`)を1列追加して単一PKにする
- 他テーブルへの参照は `validate.check_relation` で明示する(`field` / `table` / `target_field`)
- Enumの `name` はスプレッドシート入力用の表示名(日本語可)、`key` は生成コード上の識別子
  (英数字必須)。分けて持つ

## 2. データ投入

- 手動運用: `Shared/master-data/csv/<input_csv>.csv` に直接CSVを配置する
- Google Sheets運用: シートのタブ名を `input_csv` の値と一致させ、`./run.sh download` で取得
- CSVの行レイアウトは固定: 1-2行目コメント、3行目型情報(コメント扱い)、4行目ヘッダー
  (yamlの`fields[].name`と一致させる)、5行目〜データ

## 3. 生成・配置

`master-data-pipeline` スキルの手順に従って `validate` → `server` / (制約付きで) `client` を実行する。

## 4. サーバー側でAPIから参照可能にする(テーブル追加時のみ、要手動対応)

**重要な既知のギャップ**: `server_codegen` がRust struct(`Server/src/master/generated/*.rs`)と
JSON(`Server/master_data/*.json`)を生成・配置しても、それだけではAPIサーバーから参照できない。
現状(`Shared/docs/progress.md`参照)、DBキャッシュ機構(`src/master/cache.rs`)とシード投入
(`src/bin/seed_master_data.rs`)は `pachimon` テーブルしか対応していない。新しいテーブルを
APIハンドラから使えるようにするには、以下を手動で追加する必要がある:

1. `Server/migrations/` に新テーブルのマイグレーションを追加する
2. `Server/src/master/cache.rs` の `MasterData` に新テーブルの読み込みを追加する
3. `Server/src/bin/seed_master_data.rs` に新テーブルのUPSERT処理を追加する
   (生成されたJSON `Server/master_data/<name>.json` をパースして投入する形。既存の
   `seed_pachimon` 関数を参考にする)
4. `Server/tests/master_data_test.rs` にパース・整合性テストを追加する
5. `server-dev-env` スキルの手順で `sqlx migrate run` → `cargo run --bin seed_master_data` →
   サーバー再起動、まで行って初めてDBに反映される

Enum追加のみ(既存テーブルの列が参照するだけ)の場合はこの手順は不要。

## 5. ドキュメント更新

`atlas-design-docs-sync` スキルを参照。最低限:

- `Shared/docs/design/architecture.md` の「マスターデータ設計」節にテーブル/Enumの説明を追記
- `Shared/docs/progress.md` の実装状況表・残タスク一覧を更新
  (特にstep 4のDBキャッシュ対応が未着手なら「今回発見したギャップ」的に明記する)
