# Atlas

クライアント(Unity)とサーバー(Rust)を組み合わせて、デバイス認証・チャット・スカウト・対戦マッチング・リアルタイムバトル(C#/MagicOnion)を持つミニマムなオンラインゲームバックエンドを構築するプロジェクト。

## このアプリについて

デバイス認証(ゲスト型アカウント)で始める、リアルタイム対戦モンスター収集ゲーム。
「パチモン」と呼ばれるモンスターをスカウトで集め、パーティを組んで
他プレイヤーとオンライン対戦する。

### コアループ

```
① スカウト(ガチャ)でパチモンを集める
       ↓
② パーティを編成する(最大6体)
       ↓
③ マッチングに参加し、対戦相手とマッチする
       ↓
④ リアルタイム対戦(3体選出制)
       ↓
① へ戻る(対戦結果で得られる石で②へ)
```

チャットは上記ループとは独立した、全プレイヤー共通の1チャンネル機能。

### 機能一覧

| 機能 | 概要 | 詳細設計書 |
| --- | --- | --- |
| アウトゲーム | デバイス認証、プレイヤー管理、チャット、所持パチモンの管理(パーティ編成・技の付け替え) | [Shared/docs/design/outgame.md](Shared/docs/design/outgame.md) |
| スカウト(ガチャ) | 石を消費してパチモンを排出する抽選機能 | [Shared/docs/design/scout.md](Shared/docs/design/scout.md) |
| バトル | マッチング〜リアルタイム対戦〜結果記録 | [Shared/docs/design/battle.md](Shared/docs/design/battle.md) |

### システム構成(概要)

```
Unity Client
   │ REST (HTTP)              │ gRPC/StreamingHub (MagicOnion)
   ▼                          ▼
Rust/Axum API Server    C#/MagicOnion Server
(認証/スカウト/チャット/     (リアルタイム対戦のみ)
 マッチング/DB書き込み)          │
   ▲──────── 内部API(結果報告) ──┘
   │
 MySQL (唯一のデータストア、書き込みはRust経由に統一)
```

## 構成

```
Atlas/
├── Client/                 # Unityクライアント(プロジェクト構築・ライブラリ導入まで完了、画面/通信/ゲームロジックの実装は未着手)
├── Server/                 # Rustバックエンド(REST API、実装中)
├── Shared/                 # クライアント/サーバー間の共有定義
├── master-data-pipeline/   # マスターデータ生成パイプライン(スプレッドシート→各プラットフォーム向け出力、自作)
└── api-codegen/            # OpenAPI仕様書→Unity向けDTO・通信APIクライアント生成ツール(自作)
```

詳細設計は以下を参照:

- [Shared/docs/design/architecture.md](Shared/docs/design/architecture.md) — 全体構成・命名規則・マスターデータ設計などの横断的な内容
- [Shared/docs/design/client-architecture.md](Shared/docs/design/client-architecture.md) — Unity Client側の画面遷移・DI・Mock/Real切り替えの枠組み
- `Server/docs/notes/design.md` — サーバー全体構成(MagicOnion含む)
- `Server/docs/notes/api-design.md` — REST API仕様・DB設計

## 設計上のポイント

- **デバイス認証**: 会員登録なしのゲスト型認証。`secret_key`はArgon2でハッシュ化して保存し、1つの`device_id`につき有効なアクセストークンは常に1つ(再認証時は上書き)
- **共通認証Extractor**: axumの`FromRequestParts`を実装した`AuthenticatedDevice`により、要認証エンドポイントの`Authorization: Bearer`検証ロジックを一元化
- **マスターデータ管理**: 正はMySQL。`master-data-pipeline`(スプレッドシート→CI→DB seed)で投入し、APIサーバーは起動時にDBから全マスタを読み込んでメモリキャッシュを参照する方針。サーバー1台構成という規模に対して、常時ポーリングでの無停止反映のような複雑さは現時点で導入しない判断とした(詳細は`Server/docs/notes/api-design.md`の「マスターデータ管理」セクション参照)
- **コード生成ツールの自作**: `master-data-pipeline`(マスターデータ生成)と`api-codegen`(OpenAPI仕様書→Unity向けコード生成)は、既存OSSでの代替(`api-codegen`は`openapi-generator`/`NSwag`)を検討した上で自作した専用ツール。特定プロジェクトに依存しない汎用ツールとして設計されている(詳細は各ツールの`README.md`、検討経緯は[Shared/docs/feature-api-codegen.md](Shared/docs/feature-api-codegen.md)を参照)

## 技術スタック

### Client

- Unity 6.6

**アーキテクチャ・DI**

- `VContainer`(DIコンテナ)
- `UnityScreenNavigator`(画面遷移)
- `Addressables` / `SmartAddresser` / `AddressDefinitionGenerator`(CyberAgent製、アドレスの一元管理・アクセス用定数の自動生成)

**非同期・リアクティブ**

- `UniTask`(非同期処理)
- `R3`(Reactive Extensions)

**通信**

- `MagicOnion`(gRPC/StreamingHubによるリアルタイム通信。C#/MagicOnion対戦サーバーとの通信を担当)
- `YetAnotherHttpHandler`(MagicOnion/gRPC通信用のHTTPハンドラ実装)
- `MessagePack for C#`(シリアライズ)
- REST API(Rust/Axumサーバー宛)は`api-codegen`(自作、後述)が生成する`UnityWebRequest`+`UniTask`ベースの通信クライアントを使用

**自作パッケージ**

- `Supplement` / `Supplement.ZeroMessenger`([chinpanGX/Supplement](https://github.com/chinpanGX/Supplement)。暗号化・永続化・階層メッセージング等の共通ユーティリティを提供する自作Unityパッケージ。VContainer/UniTask/Addressablesと連携)

**パッケージ管理・開発支援**

- `NuGetForUnity`(NuGetパッケージのUnity導入)
- `uLoopMCP`(Claude CodeなどのAIエージェントからUnity Editorを操作するためのMCPツール)

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
