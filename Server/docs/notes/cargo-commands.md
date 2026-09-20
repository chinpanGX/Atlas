# メモ: よく使うcargoコマンド一覧

開発時に日常的に使うコマンドをまとめる。初回セットアップの手順自体は`setup.md`を参照。

## 前提

以下のコマンドは全て`Atlas/Server`ディレクトリ(`Cargo.toml`と同じ階層)で実行する。
DBに繋ぐコマンド(`cargo run`本体、テスト)は、事前に`make up`でMySQLコンテナを起動し、
`.env`に`DATABASE_URL`が設定されていることが前提(`setup.md`参照)。

## ビルド

```bash
cargo build          # デバッグビルド(コンパイルエラーの確認のみなら cargo check の方が速い)
cargo build --release
cargo check           # コンパイルできるかだけを高速にチェック(バイナリは生成しない)
```

## 実行

```bash
cargo run                              # 本体のAPIサーバー(src/main.rs)を起動
cargo run --bin seed_master_data       # マスタデータをJSON→DBへUPSERT投入するコマンド
cargo run --example setup_check        # 環境構築確認用の最小サーバー(setup.md参照)
```

`src/bin/`配下のファイルは`cargo run --bin <ファイル名(拡張子無し)>`、`examples/`配下は
`cargo run --example <ファイル名(拡張子無し)>`で実行する(`--bin`/`--example`の使い分けと
フォルダ名の単数形/複数形の注意点は`cargo-examples.md`参照)。

## テスト

```bash
cargo test                              # 全テスト実行(tests/配下 + ユニットテスト)
cargo test --test device_api_test       # tests/device_api_test.rs だけ実行
cargo test test_register_device         # テスト関数名で絞り込み(部分一致)
cargo test -- --nocapture                # println!等の出力をテスト成功時も表示する
```

各テストは`#[sqlx::test]`を使っており、実行のたびに独立したテスト用DBを自動作成・破棄する
(既存の`atlas_dev`データを汚さない)。そのためMySQLコンテナ(`make up`)が起動している
必要がある。

## その他

```bash
cargo fmt             # コードフォーマット
cargo clippy           # Lint(未導入ならプロジェクトに追加を検討)
```
