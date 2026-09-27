# Atlas

クライアント(Unity)とサーバー(Rust)を組み合わせて、デバイス認証・チャット・スカウト・対戦マッチング・リアルタイムバトル(C#/MagicOnion)を持つミニマムなオンラインゲームバックエンドを構築するプロジェクト。

## このアプリについて

デバイス認証(ゲスト型アカウント)で始める、リアルタイム対戦モンスター収集ゲーム。
「パチモン」と呼ばれるモンスターをスカウトで集め、パーティを組んで
他プレイヤーとオンライン対戦する。

### コアループ

```
① スカウトでパチモンを集める(ジェムを使う)
       ↓
② 育成する(技の付け替え、ステータスポイントの割り振り)
       ↓
③ パーティを編成する(最大6体)
       ↓
④ マッチングに参加し、対戦相手と組む
       ↓
⑤ パーティを見せ合って3体を選び、リアルタイムで対戦する
       ↓
① へ戻る(勝つとジェムがもらえる)
```

ゲームの内容とルールは[仕様概要書](Shared/docs/game-spec.md)、各機能の作りは[設計書](Shared/docs/design.md)を参照。

### 機能一覧

| 機能 | 概要 | 仕様 | 設計 |
| --- | --- | --- | --- |
| アカウント | 会員登録なしのデバイス認証、プレイヤー作成、所持データの同期 | [3. アカウント](Shared/docs/game-spec.md#3-アカウント) | [3.2 認証](Shared/docs/design.md#32-認証) |
| パーティ編成・育成 | パーティ編成、技の付け替え、ステータスポイントの割り振り(追加予定) | [5. パーティ編成・育成](Shared/docs/game-spec.md#5-パーティ編成育成) | [3.5 パーティ編成・技の付け替え](Shared/docs/design.md#35-パーティ編成技の付け替え) |
| スカウト(ガチャ) | ジェムを使い、候補10体から1体を選んで入手する | [6. スカウト](Shared/docs/game-spec.md#6-スカウト) | [3.7 スカウト](Shared/docs/design.md#37-スカウト) |
| ショップ | ジェムで育成チケットを購入する(追加予定) | [7. ショップ](Shared/docs/game-spec.md#7-ショップ追加予定) | — |
| 対戦 | マッチング、パーティの見せ合いと選出、リアルタイム対戦、結果の記録 | [8. 対戦](Shared/docs/game-spec.md#8-対戦) | [7. バトルサーバー](Shared/docs/design.md#7-バトルサーバー) |
| チャット | 全プレイヤー共通の1チャンネル(画面は未実装) | [10. チャット](Shared/docs/game-spec.md#10-チャット) | [3.6 チャット](Shared/docs/design.md#36-チャット) |

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
├── Client/                 # Unityクライアント(実装状況は Shared/docs/progress.md 参照)
├── Server/                 # Rustバックエンド(REST API、実装中)
├── BattleServer/           # C#/MagicOnionのリアルタイム対戦サーバー
├── Shared/                 # クライアント/サーバー間の共有定義
├── master-data-pipeline/   # マスターデータ生成パイプライン(スプレッドシート→各プラットフォーム向け出力、自作)
└── api-codegen/            # OpenAPI仕様書→Unity向けDTO・通信APIクライアント生成ツール(自作)
```

開発環境の構築・起動手順は[DEVELOPMENT.md](DEVELOPMENT.md)を参照。

ドキュメント:

- [Shared/docs/game-spec.md](Shared/docs/game-spec.md) — 仕様概要書(ゲームの内容とルール)
- [Shared/docs/design.md](Shared/docs/design.md) — 設計書(APIサーバー・クライアント・バトルサーバー・DB・マスターデータ)
- [Shared/docs/progress.md](Shared/docs/progress.md) — 実装の進み具合と残タスク

## 設計上のポイント

- **デバイス認証**: 会員登録なしのゲスト型認証。`secret_key`はArgon2でハッシュ化して保存し、1つの`device_id`につき有効なアクセストークンは常に1つ(再認証時は上書き)
- **共通認証Extractor**: axumの`FromRequestParts`を実装した`AuthenticatedDevice`により、要認証エンドポイントの`Authorization: Bearer`検証ロジックを一元化
- **マスターデータ管理**: 正はMySQL。`master-data-pipeline`(スプレッドシート→CI→DB seed)で投入し、APIサーバーは起動時にDBから全マスタを読み込んでメモリキャッシュを参照する方針。サーバー1台構成という規模に対して、常時ポーリングでの無停止反映のような複雑さは現時点で導入しない判断とした(詳細は[設計書](Shared/docs/design.md#5-マスターデータ)の「5. マスターデータ」参照)
- **コード生成ツールの自作**: `master-data-pipeline`(マスターデータ生成)と`api-codegen`(OpenAPI仕様書→Unity向けコード生成)は、既存OSSでの代替(`api-codegen`は`openapi-generator`/`NSwag`)を検討した上で自作した専用ツール。特定プロジェクトに依存しない汎用ツールとして設計されている(詳細は各ツールの`README.md`、検討経緯は[Shared/docs/notes/feature-api-codegen.md](Shared/docs/notes/feature-api-codegen.md)を参照)

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
