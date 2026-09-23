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
cargo run --bin seed_scout_banners  # 常設スカウトバナーを投入
cargo run                           # APIサーバー起動(マスタは起動時に読み込む)
```

`make setup`はテーブルを作るだけで、マスターデータやバナーは入らない。未投入のまま起動すると
`POST /signup`がスターター編成(`starter_party_slots`)を複製できず500になり、
`GET /scout/banners`は空配列を返す。

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
cargo run --bin seed_scout_banners  # 常設スカウトバナーを再投入
# 起動中のAPIサーバーは再起動する(古い=空のマスタをメモリに持ったままのため)
```

`make db-reset`はマスターデータ・バナーも含めて全テーブルを空にするため、上の2つのseedと
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
`starter_party_slots` / `items`の6テーブル(`type_chart`はDB投入対象外)。
このうち起動時にメモリへキャッシュするのは`pachimon` / `move_group_moves` /
`starter_party_slots` / `items`(`src/master/cache.rs`)。新しいマスタテーブルを投入対象に
加える場合は、`master-data-schema-add`スキルの手順に従って`seed_master_data.rs`
(必要なら`cache.rs`も)を拡張する。

`scout_banners`はマスターデータ(`master-data-pipeline`)の対象外で、
`cargo run --bin seed_scout_banners`が常設バナー1件(`SCOUT000000000000000000001`、
1回150ジェム)を固定IDでUPSERTする。何度実行してもよい。バナーはリクエストのたびに
DBから読むため、投入後にAPIサーバーを再起動する必要はない。

## APIサーバーの起動と通信ログ

```bash
cargo run   # APIサーバー本体(default-run = "Server")
```

ログは`cargo run`したターミナルに出る(Docker Desktopには出ない。APIサーバーはコンテナ化していない)。
出力レベルは`RUST_LOG`(`tracing-subscriber`の`EnvFilter`)で切り替える。`.env`に書いてもよい。

| 見たいもの | `RUST_LOG` |
|---|---|
| method/uri/ステータス/処理時間のみ(デフォルト) | 未指定(`info`) |
| + リクエスト/レスポンスのボディ | `info,Server=debug` |
| + 実行SQL | `info,Server=debug,sqlx::query=debug` |

```bash
RUST_LOG="info,Server=debug,sqlx::query=debug" cargo run
```

```
DEBUG request{method=POST uri=/devices}: Server::http_log: request body body={"secretKey":"***"}
DEBUG request{method=POST uri=/devices}: sqlx::query: ... INSERT INTO devices ...
DEBUG request{method=POST uri=/devices}: Server::http_log: response body status=200 OK body={"deviceId":"..."}
 INFO request{method=POST uri=/devices}: tower_http::trace::on_response: finished processing request latency=273 ms status=200
```

- ボディ出力は`src/http_log.rs`のミドルウェアが行う。`secretKey`/`accessToken`は`***`に伏字にし、
  `Authorization`ヘッダーは出力しない。4096文字を超えるボディは切り詰める
- Swagger UIのHTML/JSなど、`application/json`・`text/plain`以外のレスポンスはボディを出さない
- `GET /chat/poll`はクライアントが定期的に呼ぶため、DEBUGでは量が多くなる
- クライアント側から見た送受信は、Unity Consoleの`[API] --> ...`/`[API] <-- ...`で確認できる
  (Editor・開発ビルドのみ。`Infrastructure/Api/UnityApiRequestLogger.cs`)

## テスト

```bash
cargo test
```

- `tests/{auth,battle,chat,device,player,scout}_api_test.rs`: `tower::ServiceExt::oneshot` によるAPIレベル結合テスト
- `tests/master_data_test.rs` や一部のservice層テストは `sqlx::test` を使い、実際のDBを必要とする
  → 事前に `make up`(または `make setup`)でMySQLコンテナが起動していることを確認する
- DB不要な単体テストは `src/` 内の `#[cfg(test)] mod tests` にある(例: `src/master/mod.rs` の
  マスタデータパース検証)
