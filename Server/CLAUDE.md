# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Rust/axum製のREST APIサーバー。プロジェクト全体のアーキテクチャと共通ルールは[ルートのCLAUDE.md](../CLAUDE.md)。
環境構築・コマンドは[DEVELOPMENT.md](../DEVELOPMENT.md)と`server-dev-env`スキル。

## アーキテクチャ

### レイヤー

```
routes.rs   ルーティング(create_router)。エンドポイントを足すときはここに.route(...)を足す
api/        リクエスト/レスポンスの型とハンドラ。utoipaでOpenAPIの注釈を付ける
service/    業務処理とDBアクセス
model/      DBのテーブルに対応する構造体
master/     マスターデータの型(generated/、生成物)と起動時に読み込むキャッシュ(cache.rs)
```

| ファイル | 役割 |
|---|---|
| `extractor.rs` | 認証。`AuthenticatedDevice`(`Authorization: Bearer`を検証して`device_id`を渡す)と`InternalService`(内部APIの`X-Internal-Secret`を定数時間で比較する)。ハンドラの引数に足すだけで認証が掛かる |
| `error.rs` | `AppError` → HTTPステータス(`BadRequest`→400、`Unauthorized`→401、`NotFound`→404、`Conflict`→409、`InternalError`→500) |
| `state.rs` | `AppState`(DB接続プール・`Arc<MasterData>`・マッチングの待機列)。`with_state`で全ハンドラに共有する |
| `openapi.rs` | `ApiDoc`(utoipa)。`/swagger-ui`で確認でき、`export_openapi`で`Shared/api/openapi.yaml`に出力する。内部APIは載せない |
| `http_log.rs` | リクエスト/レスポンスの本文のログ(`RUST_LOG`で出し分け) |
| `main.rs` | 起動。マスタの読み込み、結果報告が届かない対戦を打ち切る定期処理(`battle_service::spawn_stale_match_cleanup`)の起動 |

機能ごとに`api/*.rs`と`service/*.rs`が対応する: `auth` / `battle`(マッチング) / `chat` / `debug`(開発用) /
`device` / `internal`(BattleServerからの内部API) / `player` / `scout`。

### 状態の持ち方

- マスターデータは起動時にDBから全件読み込み、`Arc<MasterData>`としてメモリに持つ。リクエストのたびに
  マスタのテーブルへ問い合わせない。更新の反映は`seed_master_data`での再投入とサーバーの再起動で行う
- マッチングの待機列と成立した結果は、DBではなく`AppState`のメモリに持つ(1台構成のため)。
  `POST /battle/queue`の中で同期的に組み、バックグラウンドタスクは使わない
- マスターデータの生成物(`src/master/generated/`、`master_data/*.json`)は手で編集しない。
  `seed_master_data`が投入するテーブルを増やすときは`master-data-schema-add`スキルに従い、
  `seed_master_data.rs`(必要なら`cache.rs`も)を拡張する
- スカウトのバナー(`scout_banners`)はマスターデータの対象外で、`seed_scout_banners`が投入する

## コーディングルール

### API

- 識別子もなるべくURLのパスではなくリクエストの本文に置く(`/edit/party`、`/chat/send`のようなRPC寄りの命名)
- レスポンスの型に`Option<T>`を使わない(`nullable`になり`api-codegen`が扱えない)。無いことは空の配列・空文字・
  行が無いことで表す
- 所持データを変えるAPIは`PlayerDiffDto`(`api/player.rs`)を返す。変化の無い種別も空配列で必ず含める
- ハンドラ・DTOには`#[utoipa::path(...)]`/`ToSchema`を付け、`openapi.rs`の`ApiDoc`に登録する(タグも必須。
  `api-codegen`がタグ単位でクライアントを作るため)。内部API(`api/internal.rs`)は登録しない
- API(ハンドラ・DTO)を変えたら`api-codegen`スキルでUnity向けの型を生成し直す

### エラーとトランザクション

- エラーは`AppError`で返す。DBのエラーは`.map_err(|_| AppError::InternalError)?`で変換する(DBの詳細を
  クライアントへ返さない)
- 複数のテーブルを書き換える処理は1つのトランザクションにまとめる(プレイヤー作成と初期付与、スカウトの消費と
  候補の保存、結果報告と報酬付与など)。トランザクション内で呼ぶ関数は`&mut Transaction<'_, MySql>`を受け取る
- 同時に届くと二重に処理されるもの(結果報告)は、対象の行を`SELECT ... FOR UPDATE`でロックしてから確かめる
- SQLは実行時に組み立てる`sqlx::query`/`query_as`を使う(コンパイル時に検査するマクロ`query!`は使っていない)

### ドキュメントコメント

- 公開する関数・型には`///`で概要を書き、失敗する関数には`# Errors`節でどの`AppError`をいつ返すかを書く
- ハンドラのドキュメントコメントはOpenAPIの説明文になる(`Shared/api/openapi.yaml`に出る)

### Lint・フォーマット

- `Cargo.toml`の`[lints.clippy]`: `unwrap_used`・`expect_used`は警告(起動処理・seedでは意図して使う)、
  `dbg_macro`・`todo`・`unimplemented`は禁止
- フォーマットは`make fmt`(`make fmt-check`で確認)。**生の`cargo fmt`は使わない**。生成物
  (`src/master/generated/`)の並びが変わり、次に生成したときとの差分が出続けるため(`rustfmt`の除外指定は
  stableでは効かないので、`make fmt`が生成物を除いたファイルに対して実行する)

### マイグレーション

- `migrations/`に追加してから`make migrate`で適用する。取り消しは`make migrate-revert`
- 適用済みのマイグレーションの中身を変えると(コメントも含む)、DBに記録されたチェックサムと合わなくなり
  `sqlx migrate run`が失敗する。スキーマの変更は新しいマイグレーションで行い、コメントだけを直した場合は
  ローカルDBの`_sqlx_migrations.checksum`をファイルのSHA-384で更新する(またはDBを作り直す)

## テスト

- `tests/*_api_test.rs`: `tower::ServiceExt::oneshot`でRouterに直接リクエストを送るAPIの結合テスト。
  各ファイルの先頭に、そのファイルのテストの一覧と確かめる内容を書いている
- `#[sqlx::test]`はテストごとに独立したDBを作って捨てる(`atlas_dev`を汚さない)。MySQLコンテナが起動している必要がある
- マスタが要るテストは、必要な行だけをテストの中でDBへ直接入れる(`seed_test_master_data`等)
- DB不要な単体テストは`src/`内の`#[cfg(test)] mod tests`に置く
