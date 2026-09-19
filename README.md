# Atlas

クライアント(Unity)とサーバー(Rust)を組み合わせて、デバイス認証・チャット・パチモンスカウト・対戦マッチングを持つミニマムなオンラインゲームバックエンドを構築する学習プロジェクト。

## 構成

```
Atlas/
├── Client/                 # Unityクライアント(未実装)
├── Server/                 # Rustバックエンド(REST API、実装中)
├── Shared/                 # クライアント/サーバー間の共有定義
└── master-data-pipeline/   # マスターデータ生成パイプライン(スプレッドシート→各プラットフォーム向け出力)
```

詳細設計は`Server/docs/notes/design.md`(サーバー全体構成・MagicOnion含む)と
`Server/docs/notes/api-design.md`(REST API仕様・DB設計)を参照。

## 実装状況(Server)

### 実装済み(結合テスト16件で動作確認済み、`cd Server && cargo test`)

| メソッド | パス | 説明 |
|---|---|---|
| POST | `/devices` | デバイス新規登録 |
| POST | `/devices/authenticate` | デバイス認証・アクセストークン取得 |
| GET | `/auth/verify` | アクセストークン検証 |
| POST | `/players` | プレイヤー作成 |
| GET | `/players/me` | 自分のプレイヤー情報取得 |
| POST | `/chat/send` | メッセージ送信 |
| GET | `/chat/poll` | メッセージ受信/取得 |

### 未実装

- パチモン・スカウト系(`/scout/banners`, `/scout/banners/{id}/pull`, `/players/me/pachimon`, `/players/me/party`等)
- マッチング系(`/battle/queue`等)
- 内部API(`/internal/battle/result`)
- MagicOnion(C#)によるリアルタイム対戦サーバー

## 設計上のポイント

- **デバイス認証**: 会員登録なしのゲスト型認証。`secret_key`はArgon2でハッシュ化して保存し、1つの`device_id`につき有効なアクセストークンは常に1つ(再認証時は上書き)
- **共通認証Extractor**: axumの`FromRequestParts`を実装した`AuthenticatedDevice`により、要認証エンドポイントの`Authorization: Bearer`検証ロジックを一元化
- **マスターデータ管理**: 正はMySQL。`master-data-pipeline`(スプレッドシート→CI→DB seed)で投入し、APIサーバーは起動時にDBから全マスタを読み込んでメモリキャッシュを参照する方針。サーバー1台構成という規模に対して、常時ポーリングでの無停止反映のような複雑さは現時点で導入しない判断とした(詳細は`Server/docs/notes/api-design.md`の「マスターデータ管理」セクション参照)

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

- レイヤードアーキテクチャ(`api` / `service` / `model` の3層構造 + 横断的な`extractor`)
  - `api`:リクエスト/レスポンスの型定義とハンドラ
  - `service`:認証処理・DB操作などのコアロジック
  - `model`:DBテーブルに対応するデータ構造
  - `extractor`:要認証エンドポイント共通の認証チェック(axumの`FromRequestParts`)

**テスト**

- 標準テスト機構(`cargo test`)
- DB込みの統合テスト:`sqlx::test`
- APIレベルの結合テスト:`tower::ServiceExt::oneshot`

**API動作確認・開発ツール**

- Postman(エンドポイントの疎通確認)
- IDE:JetBrains RustRover
