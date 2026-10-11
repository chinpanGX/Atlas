# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Rust製のAPIサーバー。クライアント向けのAPIはgRPC(tonic + gRPC基盤VolTech gRPCのクレート`voltech-grpc-*`)、
BattleServer向けの内部APIはREST(axum)で、同じプロセスの別のポートで出す。プロジェクト全体のアーキテクチャと共通ルールは
[ルートのCLAUDE.md](../CLAUDE.md)。環境構築・コマンドは[DEVELOPMENT.md](../DEVELOPMENT.md)と`server-dev-env`スキル。
基盤の使い方は基盤のwiki(`docs/wiki/guide/`、特に`add-api.md`・`server-reference.md`)。

## アーキテクチャ

### レイヤー

```
grpc/       gRPCのサービスの実装(ハンドラ)と、サーバーの組み立て(grpc.rsのserve。Layerの順番に意味がある)
proto.rs    build.rsがShared/protoとShared/errors.tomlから生成したコード(proto::atlas::v1、proto::errors)
api/        内部API(REST)のリクエスト/レスポンスの型とハンドラ
routes.rs   内部APIのルーティング(create_router)
service/    業務処理とDBアクセス
model/      DBのテーブルに対応する構造体
master/     マスターデータの型(generated/、生成物)と起動時に読み込むキャッシュ(cache.rs)
```

| ファイル | 役割 |
|---|---|
| `grpc.rs` | gRPCサーバーの組み立て。`ServerStatusLayer`(メンテナンス・強制アップデート)→ `RpcMethodLayer` → サービスごとに`AuthLayer` → `DateChangeLayer`。アプリのサービスには必ず`AuthLayer`と`DateChangeLayer`を付ける |
| `grpc/context.rs` | `AuthContext`からサインアップ済みのプレイヤーIDを取り出す(未作成なら`PLAYER_NOT_FOUND`)、`AppError`を`INTERNAL`にする |
| `grpc/player_lookup.rs` | 基盤の`PlayerResolver`の実装(`players`を引いてプレイヤーが作成済みかを返す) |
| `extractor.rs` | 内部APIの認証`InternalService`(`X-Internal-Secret`を定数時間で比較する)。ハンドラの引数に足すだけで認証が掛かる |
| `error.rs` | `AppError`(サービス層のエラー)と、内部APIのHTTPステータスへの変換 |
| `db.rs` | 起動時のマイグレーション(基盤のテーブル → Atlasのテーブル) |
| `state.rs` | `AppState`(DB接続プール・`Arc<MasterData>`・マッチングの待機列)。gRPCのサービスと内部APIで共有する |
| `http_log.rs` | 内部APIのリクエスト/レスポンスの本文のログ(`RUST_LOG`で出し分け) |
| `main.rs` | 起動。サーバーの状態の設定の読み込み、マイグレーション、マスタの読み込み、gRPCと内部APIの起動、結果報告が届かない対戦を打ち切る定期処理(`battle_service::spawn_stale_match_cleanup`)。開発用の`DebugService`は環境変数`ENABLE_DEBUG_API`で登録するかを決める(未設定ならデバッグビルドだけ) |
| `config/server_status.toml` | メンテナンス・最低バージョン・ゲーム内の日付が変わる時刻(5秒ごとに読み直す。環境変数`SERVER_STATUS_PATH`で場所を変えられる) |

### 状態の持ち方

- マスターデータは起動時にDBから全件読み込み、`Arc<MasterData>`としてメモリに持つ。リクエストのたびに
  マスタのテーブルへ問い合わせない。更新の反映は`seed_master_data`での再投入とサーバーの再起動で行う
- マッチングの待機列と成立した結果は、DBではなく`AppState`のメモリに持つ(1台構成のため)。
  待機列への参加の中で同期的に組み、バックグラウンドタスクは使わない。成立した結果は状況の確認で返しても消さず、
  次の参加・離脱か、内部APIへの結果報告で消す
- マスターデータの生成物(`src/master/generated/`、`master_data/*.json`)は手で編集しない。
  `seed_master_data`が投入するテーブルを増やすときは`master-data-schema-add`スキルに従い、
  `seed_master_data.rs`(必要なら`cache.rs`も)を拡張する
- スカウトのバナー(`scout_banners`)はマスターデータの対象外で、`seed_scout_banners`が投入する
- デバイス・アクセストークン・冪等性の直前のレスポンスは、基盤のテーブル(`devices`・`access_tokens`・`last_responses`)。
  基盤の`player_id`は`ulid::Ulid`(DBは`BINARY(16)`)、Atlasのテーブルは`CHAR(26)`の文字列なので、つなぎ目で
  `to_string()`/`from_string()`で変える

## コーディングルール

### API(gRPC)

- APIの定義は`Shared/proto/atlas/v1/*.proto`が唯一の正本。RPC・メッセージ・フィールドの直前のコメントがAPIの仕様になる
  (値の形・どのエラーをいつ返すかまで書く)。サービスを持つ`.proto`を足したら`build.rs`の`PROTO_FILES`にも足す
- 冪等性: 書き込むRPCは`idempotency_level`を書かない(冪等性の対象)。読むだけなら`NO_SIDE_EFFECTS`、
  何回実行しても同じ結果になるなら`IDEMPOTENT`
- ハンドラは業務処理を`Transactions::run`の中で動かす。冪等性の対象のRPCは`player_id`(`Ulid`)を渡す。
  サービス層の関数は、渡されたトランザクション(`&mut Transaction<'_, MySql>`)か`impl MySqlExecutor`で読み書きし、
  自分でトランザクションを始めない
- 所持データを変えるRPCは、レスポンスに`PlayerDiff player_diff`を持たせる(変わらない種別は`None`)
- 業務エラーは`Shared/errors.toml`に足し、`voltech_grpc_error::failed_precondition(proto::errors::XXX, "...")`で返す。
  内部エラーは`voltech_grpc_error::internal(...)`(詳細はログだけ)。消したreasonは使い回さない
- `.proto`・`errors.toml`を変えたら、ルートで`make proto-gen`・`make errors-gen`を実行してUnity向けのC#も生成し直す
- フィールド番号・名前は一度使ったら使い回さない(消したら`reserved`)

### 内部API(REST)

- 識別子もなるべくURLのパスではなくリクエストの本文に置く
- レスポンスの型に`Option<T>`を使わない。無いことは空の配列・空文字・行が無いことで表す
- エラーは`AppError`で返す。DBのエラーは`.map_err(|_| AppError::InternalError)?`で変換する(DBの詳細を
  呼び出し元へ返さない)

### トランザクション

- 複数のテーブルを書き換える処理は1つのトランザクションにまとめる(プレイヤー作成と初期付与、スカウトの消費と
  候補の保存、結果報告と報酬付与など)
- 同時に届くと二重に処理されるもの(結果報告、サインアップ)は、対象の行を`SELECT ... FOR UPDATE`でロックしてから確かめる
- SQLは実行時に組み立てる`sqlx::query`/`query_as`を使う(コンパイル時に検査するマクロ`query!`は使っていない)

### ドキュメントコメント

- 公開する関数・型には`///`で概要を書き、失敗する関数には`# Errors`節でどの`AppError`をいつ返すかを書く

### Lint・フォーマット

- `Cargo.toml`の`[lints.clippy]`: `unwrap_used`・`expect_used`は警告(起動処理・seedでは意図して使う)、
  `dbg_macro`・`todo`・`unimplemented`は禁止
- フォーマットは`make fmt`(`make fmt-check`で確認)。**生の`cargo fmt`は使わない**。生成物
  (`src/master/generated/`)の並びが変わり、次に生成したときとの差分が出続けるため(`rustfmt`の除外指定は
  stableでは効かないので、`make fmt`が生成物を除いたファイルに対して実行する)

### マイグレーション

- `migrations/`に追加する。サーバーの起動時に、基盤のマイグレーションの後に流れる(`db::migrate`)。`make migrate`
  (sqlx-cli)でもAtlasの分だけを流せるが、基盤のテーブルはサーバーを起動するまで作られない
- 適用済みのマイグレーションの中身を変えると(コメントも含む)、DBに記録されたチェックサムと合わなくなり
  起動時のマイグレーションが失敗する。スキーマの変更は新しいマイグレーションで行い、コメントだけを直した場合は
  ローカルDBの`_sqlx_migrations.checksum`をファイルのSHA-384で更新する(またはDBを作り直す)
- 基盤のテーブル名(`devices`・`access_tokens`・`last_responses`)はAtlasのマイグレーションで使わない

## テスト

- `tests/*_grpc_test.rs`: 空いているポートで起動したgRPCサーバーに、tonicの生成クライアントで呼ぶ結合テスト。
  起動・デバイス登録・トークン・ヘッダー(`authorization`・`x-request-id`)の部品は`tests/common/mod.rs`
- `tests/battle_api_test.rs`: 内部APIのテスト(`tower::ServiceExt::oneshot`でRouterに直接送る)。対戦はサービスの関数で
  待機列に入れて成立させる
- 冪等性の対象のRPCは、同じRequestIDで2回送っても業務データが1回分しか変わらないことを確かめる。`IDEMPOTENT`のRPCは、
  2回呼んでも状態が同じになることを確かめる
- `#[sqlx::test(migrations = false)]`でテストごとに独立したDBを作り、`common::prepare_db`で基盤とAtlasのマイグレーションと
  最小限のマスタを入れる(`atlas_dev`を汚さない)。MySQLコンテナが起動している必要がある
- DB不要な単体テストは`src/`内の`#[cfg(test)] mod tests`に置く
