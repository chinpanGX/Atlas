---
name: server-dev-env
description: Atlas/Server(Rust/axum)のローカル開発環境(MySQL Dockerコンテナ、マイグレーション、マスターデータのDB投入、テスト)をセットアップ・起動・リセットするときに使う。cargo build/testが失敗する、DBに繋がらない、マスターデータが反映されないときに参照する。
---

# Server ローカル開発環境

作業ディレクトリは `Server/`。DB接続情報は `.env` の `DATABASE_URL` から読み込まれる
(`dotenvy`)。`sqlx` はコンパイル時クエリチェックを行うため、**`.sqlx`オフラインキャッシュが
無い現状、MySQLコンテナが起動していないと `cargo build` / `cargo check` 自体が失敗する**。

## 初回セットアップ

```bash
make setup                          # up (docker compose up -d) + wait-db + migrate
cargo run --bin seed_master_data    # マスターデータを投入
cargo run                           # APIサーバー起動(マスタは起動時に読み込む)
```

`make setup`はテーブルを作るだけで、マスターデータ(スカウトのバナーも含む)は入らない。未投入のまま起動すると
`PlayerService/SignUp`がスターター編成(`starter_party_slots`)を複製できず`INTERNAL`になり、
`ScoutService/ListBanners`は空の一覧を返す。

## 日常操作

```bash
make up        # コンテナ起動
make down       # コンテナ停止(データ保持)
make restart    # down + up
make logs       # MySQLログ追跡
make mysql      # mysqlクライアントで接続(root, パスワードなし, DB: atlas_dev)
make ps         # 状態確認
```

## DBを空にして作り直す

```bash
make db-reset                       # clean(down -v) + up + wait-db + migrate
cargo run --bin seed_master_data    # マスターデータを再投入
# 起動中のAPIサーバーは再起動する(古い=空のマスタをメモリに持ったままのため)
```

`make db-reset`はマスターデータ・バナーも含めて全テーブルを空にするため、上のseedと
APIサーバーの再起動までを1セットで行う。

DBをリセットすると、Unity Client側に保存済みのデバイス情報(`device_id`/`secret_key`)は
サーバー側に存在しなくなり、サインインできなくなる。Editorの場合は
`%USERPROFILE%/AppData/LocalLow/DefaultCompany/AtlasUnityProject/SaveData`
(Multiplayer Play Modeの仮想プレイヤーは`SaveData_VP_<id>`)を削除してから起動する。

## マイグレーション

```bash
make migrate          # sqlx migrate run
make migrate-revert    # 直近1つを取り消す
```

新しいテーブルを追加したら `Server/migrations/` に追加してから `make migrate` する。

## マスターデータをDBに反映する

`master-data-pipeline` がAPIサーバー向け成果物(`Server/master_data/*.json`,
`Server/src/master/generated/*.rs`)を生成・配置しても、**それだけではDBに入らない**。
サーバーは起動時にDBから全マスタを1回読み込みメモリキャッシュする設計
(`src/master/cache.rs`、無停止反映は非対応)のため、以下の手順を踏む:

```bash
cargo run --bin seed_master_data   # master_data/*.json を MySQL へ UPSERT
# その後サーバーを再起動して初めて新しいマスタ内容が反映される
```

`seed_master_data`が投入するのは`move_groups` / `moves` / `move_group_moves` / `pachimon` /
`starter_party_slots` / `items` / `scout_banners` / `scout_banner_rates`の8テーブル(`type_chart`はDB投入対象外)。
このうち起動時にメモリへキャッシュするのは`pachimon` / `move_group_moves` /
`starter_party_slots` / `items` / スカウトのバナーと排出率(`src/master/cache.rs`)。新しいマスタテーブルを投入対象に
加える場合は、`master-data-schema-add`スキルの手順に従って`seed_master_data.rs`
(必要なら`cache.rs`も)を拡張する。

スカウトのバナーもほかのマスタと同じく、`seed_master_data`で投入してからAPIサーバーを再起動して反映する。
`seed_master_data`はバナーの日時の形・`start_at < end_at`・排出率の重みの合計(10000)を確かめ、合わなければ何も投入せずに止まる。

## APIサーバーの起動と通信ログ

```bash
cargo run   # APIサーバー本体(default-run = "Server")
```

ログは`cargo run`したターミナル(標準出力)に出る(Docker Desktopには出ない。APIサーバーはコンテナ化していない)。
既定は1行1件のJSON。設定は環境変数で変える(`.env`に書いてもよい。見本は`.env.example`)。

| 変えたいもの | 環境変数 |
|---|---|
| ターミナルで読みやすくする | `LOG_FORMAT=text` |
| 実行したSQLも出す | `RUST_LOG=info,sqlx::query=debug` |
| ゲームログを標準出力に出す(既定はDBの`game_logs`) | `GAME_LOG_SINK=stdout` |

```bash
LOG_FORMAT=text RUST_LOG="info,sqlx::query=debug" cargo run
```

- RPCごとのspanとアクセスログ(ステータス・業務エラーのreason・処理時間)は基盤の`RpcLogLayer`が出す。`request_id`・`player_id`が
  付くので、クライアントの`x-request-id`やプレイヤーで探せる。内部エラーは原因と発生した場所がerrorで出る
- リクエスト・レスポンスの中身はログに出ない。見るときは、開発ビルドで有効なgRPCリフレクションを
  使ってgrpcurl・Postmanから呼ぶか、クライアント側で見る
- `MatchmakingService/GetQueueStatus`はクライアントが1秒ごとに呼ぶため、ログの量が多くなる
- クライアント側から見た送受信は、Unity Consoleの`[Api] → ...`/`[Api] ← ...`と、`Window > VolTech gRPC > gRPC Monitor`で確認できる
  (Editor・開発ビルドのみ)

## テスト

```bash
cargo test
```

- `tests/*_grpc_test.rs`: 空いているポートで起動したgRPCサーバーに、tonicの生成クライアントで呼ぶ結合テスト
- `tests/battle_api_test.rs`: 内部API(REST)を`tower::ServiceExt::oneshot`でRouterに直接送る結合テスト
- `tests/master_data_test.rs` や一部のservice層テストは `sqlx::test` を使い、実際のDBを必要とする
  → 事前に `make up`(または `make setup`)でMySQLコンテナが起動していることを確認する
- DB不要な単体テストは `src/` 内の `#[cfg(test)] mod tests` にある(例: `src/master/mod.rs` の
  マスタデータパース検証)
