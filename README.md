# Atlas

クライアント(Unity)とサーバー(Rust)を組み合わせて、認証・チャット機能を持つミニマムなオンラインゲームバックエンドを構築する。

## 構成

```
Atlas/
├── Client/   # Unityクライアント(未実装)
└── Server/   # Rustバックエンド
```

## 技術スタック

### Client

- Unity 6.6

**開発ツール**

- IDE:JetBrains Rider

### Server

**言語・ランタイム**

- Rust(edition 2024)
- 非同期ランタイム:`tokio`

**Webフレームワーク**

- `axum`

**データベース**

- MySQL
- DBライブラリ:`sqlx`(生SQL + コンパイル時クエリチェック)

**インフラ・実行環境**

- Docker / Docker Compose(MySQLコンテナのローカル構築)
- 環境変数管理:`dotenvy`(`.env`でDB接続情報を管理)

**アーキテクチャ**

- レイヤードアーキテクチャ(`api` / `service` / `models` の3層構造)
  - `api`:リクエスト/レスポンスの型定義とハンドラ
  - `service`:認証処理・DB操作などのコアロジック
  - `models`:DBテーブルに対応するデータ構造

**テスト**

- 標準テスト機構(`cargo test`)
- DB込みの統合テスト:`sqlx::test`
- APIレベルの結合テスト:`tower::ServiceExt::oneshot`

**API動作確認・開発ツール**

- Postman(エンドポイントの疎通確認)
- IDE:JetBrains RustRover
