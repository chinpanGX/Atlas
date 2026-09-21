---
name: master-data-pipeline
description: master-data-pipeline(Atlas/master-data-pipeline、別gitリポジトリ)のrun.sh/build.shを使って、Shared/master-data配下のスキーマ・CSVからClient/Server向け成果物を生成・配置するときに使う。スキーマやCSVを変更した後、または生成物が古い可能性があるときに実行する。
---

# master-data-pipeline の実行

`master-data-pipeline/` はAtlasとは別の`.git`を持つ独立リポジトリ(汎用ツール)。
プロジェクト固有のスキーマ・データは `Shared/master-data/` 側にあり、`config.yaml` の
`tables_dir`/`enums_dir`/`csv_dir` がそこを参照する構成になっている。

作業ディレクトリは常に `master-data-pipeline/`。

## 実行前の判断

- **ツール本体(`tools/dotnet`, `tools/rust` 配下)のコードを変更したときだけ** `./build.sh tools` が必要。
  スキーマ(`schema/`)やCSVの中身を変えただけなら不要。
- 通常のスキーマ/CSV変更後のフローは `./run.sh <command>`(ビルド済みツールを呼ぶだけ)。

## 通常フロー

```bash
cd master-data-pipeline

# 前処理(スキーマ/CSVを変更したら毎回)
./run.sh normalize-csv
./run.sh resolve-enums
./run.sh validate       # unique / check_relation を検証。ここで失敗したら生成に進まない

# サーバー(Rust API)向け一式 — このまま実行してよい
./run.sh server         # build-server + copy-server-rust + copy-server-json
```

`./run.sh server` の配置先(`config.yaml`の`copy_destinations`)は既に実パスになっている:
`server_rust_dest_dir: ../Server/src/master/generated`, `server_json_dest_dir: ../Server/master_data`。

## クライアント向けは `./run.sh client` を素のまま使わない(重要)

`./run.sh client` は内部で以下6コマンドを実行する:
`generate-csharp` → `build-client` → `copy-models` → `copy-loader` → `copy-client-bytes` → `copy-realtime-bytes`

このうち **`copy-loader` と `copy-realtime-bytes` は今のAtlasでは安全に実行できない**。
`config.yaml` の `realtime_loader_dest_dir` / `realtime_bytes_dest_dir` が
`../__REALTIME_SERVER_NOT_YET_CREATED__/...`(realtime_serverプロジェクトが未作成なため仮の
プレースホルダ)のままであり、実行するとAtlasリポジトリのルート直下に
`__REALTIME_SERVER_NOT_YET_CREATED__/` という意図しないディレクトリが生成される
(`copy-loader`はclient向けとrealtime向けに同時書き込みする実装で、片方だけスキップするオプションが無い)。

realtime_serverプロジェクトが実在するようになるまでは、個別コマンドで代替する:

```bash
./run.sh generate-csharp
./run.sh build-client
./run.sh copy-models
./run.sh copy-client-bytes
# copy-loader と copy-realtime-bytes はスキップ
#   → MasterDataLoader.cs / AesCrypto.cs はClient側に未配置のまま、という状態を許容する
```

realtime_serverプロジェクトを新規作成したら、まず `config.yaml` の
`realtime_loader_dest_dir` / `realtime_bytes_dest_dir` を実パスに書き換えてから
`./run.sh client` をフルで使ってよい。

## 全体一括(`./run.sh all`)も同じ理由で避ける

`all` は `download`(Google Sheets取得)から `client` まで含むため、上記の
`copy-loader`/`copy-realtime-bytes` 問題を引き継ぐ。Google Sheetsから取り込みたいだけなら
`./run.sh download` を単独で叩けばよい。

## その他の既知の制約(スキーマ設計時に効く)

- 型は `int` / `string` / `bool` / `enum` の4種類のみ、nullable型が無い(NULLはセンチネル値で表現)
- 複合主キー・複合UNIQUE非対応(必要なら代理キー列を1本追加する)
- 列名にRustの予約語(`type`, `move`, `match`, `loop`, `ref`, `use`, `self`, `static`, `struct`,
  `enum`, `for`, `in`, `let`, `true`, `false`, `if`, `else`, `fn`, `impl`, `trait`, `mut`, `pub`,
  `as`, `return`, `const` 等)を使うと `server_codegen` の生成物がコンパイルエラーになる
  (例: `move_type` のように接頭辞/接尾辞で回避する)

新しいテーブル/Enumをゼロから追加する手順は `master-data-schema-add` スキルを参照。
