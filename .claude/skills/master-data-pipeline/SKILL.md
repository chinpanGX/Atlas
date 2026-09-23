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

## クライアント(Unity)・BattleServer向け

```bash
./run.sh client     # generate-csharp + build-client + copy-models + copy-client-loader + copy-client-bytes
./run.sh realtime   # copy-realtime-models + copy-realtime-loader + copy-realtime-bytes(BattleServer向け)
```

- `client` はClientのみ、`realtime` はBattleServerのみに配置する(互いに書き込まない)。
- `realtime` は生成を行わずコピーだけなので、**必ず `client`(または `generate-csharp` +
  `build-client`)の後に実行する**。スキーマ/CSVを変えたら両方を続けて実行し、ClientとBattleServerの
  生成物(特に `MasterDataLoader.cs` の `ContentHash` と `masterdata.bytes`)を揃える。
- BattleServerの配置先(`config.yaml` の `realtime_*_dest_dir`):
  `BattleServer/MasterData/{Models,Enums,Bytes}` と `BattleServer/MasterData/`直下(Loader/AesCrypto)。
  `MasterData/Atlas.MasterData.csproj` がこれらを独立したDLLとしてビルドする。
  配置後は `dotnet test BattleServer/Tests/BattleServer.Tests.csproj` で復号・読み込みを確認できる
  (`MasterDatabaseFactoryTests`)。

### コピー先ディレクトリは丸ごと作り直される(注意)

Models/Enums/bytesのコピー(`copy_dir_contents`)は、配置先ディレクトリを削除してから書き込む。
そのため:
- 配置先に手書きのファイルを置かない(BattleServerの `MasterData/Models` 等も同様)
- Unity側の `Assets/Addressables/MasterData/masterdata.bytes.meta` も消えるため、`copy-client-bytes`
  の後は `git checkout -- Client/AtlasUnityProject/Assets/Addressables/MasterData/masterdata.bytes.meta`
  で復元する(GUIDが変わるとAddressablesの参照が切れる)

## 全体一括(`./run.sh all`)は避ける

`all` は `download`(Google Sheets取得、認証情報が必要)から始まり、`realtime` も含まない。
Google Sheetsから取り込みたいだけなら `./run.sh download` を単独で叩けばよい。

## その他の既知の制約(スキーマ設計時に効く)

- 型は `int`(32bit) / `long`(64bit) / `string` / `bool` / `enum` の5種類のみ、nullable型が無い(NULLはセンチネル値で表現)
- 複合主キー・複合UNIQUE非対応(必要なら代理キー列を1本追加する)
- 列名にRustの予約語(`type`, `move`, `match`, `loop`, `ref`, `use`, `self`, `static`, `struct`,
  `enum`, `for`, `in`, `let`, `true`, `false`, `if`, `else`, `fn`, `impl`, `trait`, `mut`, `pub`,
  `as`, `return`, `const` 等)を使うと `server_codegen` の生成物がコンパイルエラーになる
  (例: `move_type` のように接頭辞/接尾辞で回避する)

新しいテーブル/Enumをゼロから追加する手順は `master-data-schema-add` スキルを参照。
