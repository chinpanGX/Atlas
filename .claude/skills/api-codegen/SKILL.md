---
name: api-codegen
description: api-codegen(Atlas/api-codegen)を使って、ServerのOpenAPI仕様書からUnity向けC#のDTO・通信APIクライアントを生成・配置するときに使う。Rust側のAPI(handler・DTO)を追加/変更した後、Unity側の型が古い可能性があるときに実行する。
---

# api-codegen の実行

`api-codegen/` はプロジェクト非依存の共通ツール(Atlasリポジトリ直下、`master-data-pipeline`とは
別モジュール)。入力は`Shared/api/openapi.yaml`(Server側の`utoipa`が生成するOpenAPI仕様書)。

作業ディレクトリは常に `api-codegen/`。

## 実行前提: openapi.yamlが最新であること

Rust側のhandler・DTOを変更したら、まず`Server/`で以下を実行して入力を最新化する
(handlerに`#[utoipa::path(...)]`、DTOに`ToSchema`が付いていないと反映されない。
新規handler追加時にこれらの注釈を付け忘れていないか確認する):

```bash
cd Server
cargo run --bin export_openapi   # Shared/api/openapi.yaml を再生成
```

## 通常フロー

```bash
cd api-codegen
dotnet run -- generate   # Shared/api/openapi.yaml -> out/generated_csharp/{Dto,Client}/*.cs
dotnet run -- copy       # out/generated_csharp -> Client/Assets/Scripts/Domain.Api/(config.yamlのcopy.dest_dir)
```

`generate`と`copy`は分離されている(`master-data-pipeline`と同じ規約)。`copy`は配置先を
処理直前に削除→再作成してから書き込むため、`Client/Assets/Scripts/Domain.Api/`配下を手で
編集していた場合は失われる(生成物なので手編集しない)。

## 既知の制約(スキーマ・handler設計時に効く)

- OpenAPIの型は `string` / `integer` / `number` / `boolean` / `array` / `$ref` のみ対応。
  `nullable`・`oneOf`/`anyOf`・列挙型(文字列enum)を含むhandlerを追加すると`generate`が
  例外で停止する(Rust側でOption<T>を使う、enumをそのままレスポンスに含める、等をすると発生する)
- リクエスト/レスポンスとも`application/json`のみ対応
- クエリパラメータ(`in: query`)は未対応。パスパラメータ(`in: path`)は対応済み
  (axumの`:id`はutoipaが`in: path`として出力し、api-codegen側でメソッド引数に展開される)
- 各operationには`tag`と`operationId`が必須(utoipa側で`tag = "..."`を指定していないとエラーになる)

## 生成物を使う側の注意

- `{Tag}ApiClient`はVContainer等のDIコンテナに依存しない。認証不要なタグは
  `new {Tag}ApiClient(baseUrl)`、認証必要なタグ(OpenAPI上に`security`があるoperationを含む)は
  `new {Tag}ApiClient(baseUrl, () => currentAccessToken)`で生成する
- 生成コードは`UnityEngine.Networking`(UnityWebRequest)と`Cysharp.Threading.Tasks`(UniTask)に
  依存する。Unityプロジェクト側にUniTaskパッケージが未導入の場合はコンパイルできない
  (2026-09時点、ClientはUnityプロジェクトとして未構築のためコンパイル未確認。詳細は
  `Shared/docs/feature-api-codegen.md`の残タスク参照)

## 別プロジェクトで使い回す場合

`api-codegen/`ディレクトリを丸ごとコピーし、`config.yaml`(`input.open_api_path` /
`output.dir` / `output.namespace` / `copy.dest_dir`)をそのプロジェクトの実パスに
書き換えるだけでよい。ツール本体のソースにAtlas固有のパス・名前空間はハードコードされていない。
