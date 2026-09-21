# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Rust/Axum製のREST APIサーバー。全体像は [ルートのCLAUDE.md](../CLAUDE.md) を参照。

## セットアップ・開発環境操作

MySQL Dockerコンテナの起動・マイグレーション・リセットなどは `server-dev-env` スキルを使う
(`make setup` / `make up` / `make db-reset` 等)。DB接続情報は`.env`の`DATABASE_URL`(`dotenvy`)。
`sqlx`はコンパイル時クエリチェックを行うため、**MySQLコンテナが起動していないと`cargo build`/
`cargo check`自体が失敗する**。

## よく使うコマンド

全て`Server/`ディレクトリ(`Cargo.toml`と同じ階層)で実行する。

```bash
cargo check                              # コンパイル確認のみ(高速)
cargo build

cargo run                                # APIサーバー本体(src/main.rs)を起動
cargo run --bin seed_master_data         # マスタデータをJSON→DBへUPSERT投入
cargo run --example setup_check          # 環境構築確認用の最小サーバー

cargo test                               # 全テスト実行
cargo test --test device_api_test        # 特定の結合テストファイルのみ
cargo test test_register_device          # テスト関数名で部分一致絞り込み
cargo test -- --nocapture                 # 成功時もprintln!等を表示

cargo fmt
```

テストは`#[sqlx::test]`を使い、実行ごとに独立したテスト用DBを自動作成・破棄するため
(既存`atlas_dev`データは汚さない)、MySQLコンテナ(`make up`)が起動している必要がある。

コマンドの詳細・注意点は `docs/notes/cargo-commands.md` / `docs/notes/cargo-examples.md` を参照。

## アーキテクチャ: レイヤード構成

```
api/       リクエスト/レスポンスの型定義とハンドラ(utoipaでOpenAPI注釈)
service/   認証処理・DB操作などのコアロジック
model/     DBテーブルに対応するデータ構造
extractor  要認証エンドポイント共通の認証チェック(axumのFromRequestParts)
```

- ルーティングは`routes.rs`に集約(`create_router`)。エンドポイント追加時はここに`.route(...)`を足す
- `AuthenticatedDevice`(`extractor.rs`)が`Authorization: Bearer <token>`を検証し、`device_id`を
  ハンドラ引数として渡す。要認証エンドポイントは引数にこれを追加するだけでよい
- エラーは`AppError`(`error.rs`)に集約し、`IntoResponse`でHTTPステータスに変換
- `AppState`(`state.rs`)が`MySqlPool`と`Arc<MasterData>`を保持し、`with_state`で全ハンドラに共有
- OpenAPI仕様は`openapi.rs`の`ApiDoc`(utoipa)から生成され、`/swagger-ui`で確認可能。
  `cargo run --bin export_openapi`で`api.yaml`として出力する(Unity向けコード生成`api-codegen`の入力)

現在実装済みの機能領域: `auth` / `chat` / `device` / `player` / `scout`
(`src/api/*.rs` / `src/service/*.rs` に対応)。

## マスターデータ

- `src/master/generated/*.rs`は`master-data-pipeline`が生成する型定義(手で編集しない)
- `src/master/cache.rs`が起動時にDBから全マスタを読み込み`MasterData`としてメモリ保持する
  (無停止反映は非対応。マスタ更新後はサーバー再起動が必要)
- **現状`seed_master_data`(`src/bin/seed_master_data.rs`)は`pachimon`テーブルしか対応していない**。
  他テーブルをDB投入対象にする場合は`master-data-schema-add`スキルの手順に従い、
  `cache.rs`/`seed_master_data.rs`を先に拡張する必要がある(詳細は`Shared/docs/progress.md`)
- スキーマ/CSV変更時の生成・配置フローは`master-data-pipeline`スキルを使う

## マイグレーション

`migrations/`配下に追加してから`make migrate`(`sqlx migrate run`)を実行する。
取り消しは`make migrate-revert`。

## テスト構成

- `tests/{auth,chat,device,player,scout}_api_test.rs`: `tower::ServiceExt::oneshot`によるAPIレベル結合テスト
- `tests/master_data_test.rs`や一部のservice層テストは`sqlx::test`を使い実DBを必要とする
- DB不要な単体テストは`src/`内の`#[cfg(test)] mod tests`にある

## API変更時の連携

`api`配下のhandler/DTOを追加・変更したら、Unity側のDTO・通信クライアントが古くなるため
`api-codegen`スキルで再生成する。機能追加・変更後は`atlas-design-docs-sync`スキルで
`docs/notes/design.md` / `docs/notes/api-design.md`等の更新要否を確認する。
