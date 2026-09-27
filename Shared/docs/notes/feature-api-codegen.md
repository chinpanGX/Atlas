# handlerからAPI仕様書・Unity側の型を自動生成する仕組み

## やりたいこと

Rust(axum)側のhandlerから`api.yaml`(OpenAPI仕様書)を自動生成し、そこからUnityクライアント側のリクエスト/レスポンス型・通信APIクラスを自動生成する仕組みを導入する。API仕様書とクライアントコードの乖離を防ぐのが目的。

## 全体の流れ

```
Rustのhandler(api層)
    ↓ ①OpenAPIスキーマを自動生成
api.yaml (OpenAPI仕様書、Shared/配下に出力)
    ↓ ②独立ツール api-codegen でC#コードを生成
Unity側のRequest/Response DTO + 通信APIクラス
```

## ① Rust側:handlerからOpenAPIを生成する(実装済み)

`utoipa`クレートを使う。`ToSchema`をDTO(リクエスト/レスポンス型)に付与し、`#[utoipa::path(...)]`マクロをhandlerに付けることで、コンパイル時にOpenAPIスキーマを生成できる。既存の`api`/`service`/`model`の3層構造を変えずに導入できた。

- 全handler(`device`/`auth`/`player`/`chat`)に`#[utoipa::path(...)]`を横展開済み(当初案の「1エンドポイントで試験導入→段階的に横展開」は、既存handlerが4ファイルと小規模だったため一度に完了させた)
- `Authorization: Bearer`認証は`src/openapi.rs`の`SecurityAddon`(`Modify`実装)でOpenAPI上の`bearer_auth`セキュリティスキームとして表現(実際の検証ロジックは従来通り`AuthenticatedDevice`extractorが担う。ドキュメント表現のみの追加)
- `cargo run --bin export_openapi`(`Server/`が作業ディレクトリ)で`Shared/api/openapi.yaml`を出力する。先頭に`DO NOT EDIT`コメント付き
- `utoipa-swagger-ui`を`routes.rs`に`.merge()`し、サーバー起動中は`/swagger-ui`でブラウザから確認できる(`/api-docs/openapi.json`が仕様書本体)。Postmanでの疎通確認をこちらに置き換えていける

## ② OpenAPIからUnity(C#)向けのコードを生成する(実装済み)

**独立したツール `api-codegen`**として、Atlasリポジトリ直下(`api-codegen/`)に実装した(`master-data-pipeline`とは別モジュール。あちらはCSV/スプレッドシート駆動のマスターデータ専用のため、入力がコードから生成される`openapi.yaml`である本件とは性質が異なる)。詳細は`api-codegen/README.md`参照。

Atlas専用ではなく**プロジェクト非依存の共通ツール**として作った。プロジェクト固有のパス・名前空間はソースにハードコードせず、すべて`api-codegen/config.yaml`経由で渡す(別プロジェクトで使う場合はこのファイルを差し替えるだけでよい)。`Client/`はUnityプロジェクトとしての体裁(`ProjectSettings/`等)こそ未構築だが、`Assets/Scripts/`配下に生成物を置くこと自体は可能(`Domain.MasterData`と同じ扱い)だったため、Unityプロジェクト本体の完成を待たずに着手した。

- 入力: `Shared/api/openapi.yaml`(①で生成)
- パース: `Microsoft.OpenApi`(NuGet、v3.10.2。3.0/3.1両対応で`Microsoft.OpenApi.YamlReader`を組み合わせて読む。utoipaの出力が3.1のため1.x系では読めない)
- 出力(`api-codegen/config.yaml`の`output.dir`、Atlasでは`Client/AtlasUnityProject/Assets/Scripts/Infrastructure/Api/`へcopy):
  - `Dto/{SchemaName}.cs`:リクエスト/レスポンスDTO(POCO、Roslynで生成。`System.Text.Json`の`[JsonPropertyName]`でcamelCaseを保持)
  - `Client/{Tag}ApiClient.cs`:OpenAPIのtag単位の通信APIクラス。`UnityWebRequest`を`UniTask`でラップした薄いメソッドのみ(文字列テンプレートで生成)。**VContainerには依存せず**、`new {Tag}ApiClient(baseUrl)`(認証不要)/`new {Tag}ApiClient(baseUrl, () => token)`(認証必要)で素朴に生成できる
  - `Client/ApiRequest.cs` / `ApiException.cs`:送受信の共通ヘルパーと例外型(1回だけ生成、各ApiClientから直接参照)
- `master-data-pipeline`で確立済みの規約を踏襲:
  - `dotnet run -- generate` / `dotnet run -- copy` で生成(generate)と配置(copy)のコマンドを分離
  - 生成物の先頭に`DO NOT EDIT`コメントを付与
  - 書き込み対象ディレクトリは処理直前に削除→再作成してから書き込む(クリーンアップ)
- 現時点の制約: 型は`string`/`integer`/`number`/`boolean`/`array`/`$ref`のみ対応(`nullable`・`oneOf`/`anyOf`・列挙型は未対応、入力に含まれるとエラーで停止する)。`application/json`以外のcontent-type、クエリパラメータ(`in: query`)は未対応。パスパラメータ(`in: path`)には対応済み

検討したが採用しなかった候補:

- **`openapi-generator`**:汎用的で対応言語が多いが、UniTaskに合わせたテンプレートカスタマイズのコストが高い
- **`NSwag`**:.NET向けで型生成に強いが、Unity向けにそのまま使うにはひと手間かかる
- どちらも、生成コードの型変換ロジックだけを自作する構成(`Microsoft.OpenApi` + 自作ジェネレータ)に対して明確な優位性がないため見送り

## 進め方の方針(フェーズ)

1. [x] 全handlerにutoipaを導入し、`api.yaml`が生成できることを確認する(実装済み。当初は1エンドポイントで試験導入する想定だったが、規模が小さかったため一度に完了)
2. [x] `api-codegen`の雛形(dotnet console app)を作り、`Microsoft.OpenApi`で`openapi.yaml`をパースできることを確認する
3. [x] DTO生成を実装する(全スキーマ一括。1エンドポイント分の試験導入は行わず、規模が小さいため一度に完了)
4. [x] `UniTask`ベースの通信APIクラス生成を追加する
5. [x] 配置(`copy`)コマンドを整備する
6. [x] 生成したC#コードをUnityプロジェクト側で実際にコンパイル確認する — Unityプロジェクト本体・UniTaskパッケージ導入完了に伴い確認済み(`Atlas.Infrastructure.Api`アセンブリとしてコンパイル成功、詳細はprogress.md参照)
7. [ ] `nullable`・列挙型・クエリパラメータへの対応 — 現状のAtlas APIには存在しないため必要になってから対応する
