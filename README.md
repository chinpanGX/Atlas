# Atlas

Unityクライアント、Rustの APIサーバー、C#/MagicOnion のバトルサーバーで作る、リアルタイム対戦のモンスター収集ゲーム。

## このアプリについて

会員登録なしで始められる(端末単位のゲストアカウント)、リアルタイム対戦のモンスター収集ゲーム。
「パチモン」と呼ばれるモンスターをスカウトで集めて育成し、パーティを組んでほかのプレイヤーとオンラインで対戦する。
対戦は、本家ポケモンの対戦ルールから天候・フィールド・持ち物・特性・技の追加効果などを外した小さなルールで遊ぶ。

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
   │ REST (HTTP/JSON)              │ MagicOnion (gRPC / StreamingHub)
   ▼                               ▼
APIサーバー(Rust/axum)        バトルサーバー(C#/MagicOnion)
 認証・プレイヤー・スカウト・      対戦の進行のみ(状態はメモリ上)
 チャット・マッチング・DB書き込み        │
   ▲─────────── 内部API(REST) ──────────┘  パーティ・選出個体の取得、結果報告
   │
 MySQL(唯一のデータストア。書き込みはAPIサーバーだけが行う)
```

## 構成

```
Atlas/
├── Client/AtlasUnityProject/  # Unityクライアント
├── Server/                    # APIサーバー(Rust/axum)
├── BattleServer/              # バトルサーバー(C#/MagicOnion)、対戦相手ボット、テスト
├── Shared/                    # Client・サーバーで共有するもの
│   ├── BattleCore/            # ダメージ計算・ターン処理(ClientとBattleServerが同じソースを使う)
│   ├── BattleContracts/       # Client⇔BattleServerの通信契約
│   ├── master-data/           # マスターデータのスキーマとCSV(正本)
│   ├── api/openapi.yaml       # APIサーバーのOpenAPI仕様書(生成物)
│   └── docs/                  # 仕様概要書・設計書・Notesなど
├── master-data-pipeline/      # マスターデータの定義・自動生成ツール(submodule、自作)
├── api-codegen/               # OpenAPI → Unity向けDTO・通信クライアントの生成ツール(submodule、自作)
└── UnityScreenNavigator/      # 画面遷移ライブラリ(USN)のフォーク(submodule)
```

実装の進み具合は[Shared/docs/progress.md](Shared/docs/progress.md)を参照。

開発環境の構築・起動手順は[DEVELOPMENT.md](DEVELOPMENT.md)を参照。

ドキュメント:

- [Shared/docs/game-spec.md](Shared/docs/game-spec.md) — 仕様概要書(ゲームの内容とルール)
- [Shared/docs/design.md](Shared/docs/design.md) — 設計書(APIサーバー・クライアント・バトルサーバー・DB・マスターデータ)
- [Shared/docs/progress.md](Shared/docs/progress.md) — 実装の進み具合と残タスク

## 設計上のポイント

- **デバイス認証**: 会員登録なしのゲスト型認証。`secret_key`はArgon2でハッシュ化して保存し、1つの`device_id`につき有効なアクセストークンは常に1つ(再認証時は上書き)
- **共通認証Extractor**: axumの`FromRequestParts`を実装した`AuthenticatedDevice`により、要認証エンドポイントの`Authorization: Bearer`検証ロジックを一元化
- **DBへの書き込みの一本化**: MySQLへ書き込むのはAPIサーバーだけ。バトルサーバーはDBを持たず、パーティ・選出個体の取得と結果報告を内部API(`X-Internal-Secret`で認証)で行う。対戦の判定はバトルサーバーが権威を持ち、クライアントにはHPを%でしか送らない
- **共通モジュール**: ダメージ計算・ターン処理(`Atlas.BattleCore`)と通信契約(`Atlas.BattleContracts`)は、同じソースをUnity(ローカルパッケージ)とバトルサーバー(csproj)の両方でコンパイルする
- **マスターデータ管理**: 正本は`Shared/master-data/`のスキーマとCSV(Googleスプレッドシートからも作れる)。`master-data-pipeline`がRust・C#向けの型とデータを生成し、APIサーバーはDBへ投入したものを起動時に全件読み込んでメモリに持つ。サーバー1台構成の規模に対して、無停止での反映は現時点で導入しない判断とした(詳細は[設計書](Shared/docs/design.md#5-マスターデータ)の「5. マスターデータ」参照)
- **コード生成ツールの自作**: [master-data-pipeline](https://github.com/chinpanGX/master-data-pipeline)(マスターデータの定義・自動生成)と[api-codegen](https://github.com/chinpanGX/api-codegen)(OpenAPI仕様書→Unity向けコード生成)は、既存OSSでの代替(`api-codegen`は`openapi-generator`/`NSwag`)を検討した上で自作した専用ツール。特定プロジェクトに依存しない汎用ツールとして設計されている(詳細は各ツールの`README.md`、検討経緯は[Shared/docs/notes/feature-api-codegen.md](Shared/docs/notes/feature-api-codegen.md)を参照)

## 技術スタック

### Client

- [Unity 6.6](https://unity.com/releases/unity-6)(6000.6.2f1)

**アーキテクチャ**

オニオンアーキテクチャを採用し、レイヤーをasmdefで分けて依存の向きをコンパイルで強制する。

```text
外側  DI                               構成ルート。全レイヤーの実装を組み立てる
       ↓
      Presentation / Infrastructure    View・Presenter / API通信・Mock・MagicOnion・端末への保存
       ↓
      Application                      Service、Connection(通信)のインターフェース
       ↓
中心  Domain                           Entity、Repository(所持データ)のインターフェース
```

(矢印は依存の向き。外側のレイヤーは、それより内側のすべてのレイヤーを参照できる)

- 依存は外側から内側への一方向。Domainはどのレイヤーにも依存せず、PresentationとInfrastructureは互いを参照しない。
  通信や保存のインターフェースを内側(Application・Domain)に置き、外側のInfrastructureが実装する
- 各レイヤーの実装を組み立てるのは最も外側のDI(VContainerのLifetimeScope)だけ
- 画面はView / Presenterに分け、Presenterが具象のView(`Page`/`Modal`)をコンストラクタで受け取り、ViewのObservableを購読する
  (Viewにインターフェースは作らない)。Presenterは画面を開くたびに作る子スコープで生成し、閉じると破棄する
- 通信と手元のデータを分ける: サーバーAPIの呼び出しは`Connection`、所持データは`Repository`(メモリに保持)、
  画面から使う処理は`Service`。所持データを変えるAPIの差分(`playerDiff`)は、Connectionが受け取った直後にRepositoryへ反映する
- 通信の実装はMock/Realを差し替えられる(対戦は、クライアント内で完結するMock対戦と実サーバーをInspectorで切り替え)
- シーンはBootstrap(常駐)/ Home / Battleの3つ。DIのスコープもアプリ・シーン・画面の3段にする

**ライブラリ**

| 用途 | ライブラリ | 備考 |
| --- | --- | --- |
| DI | [VContainer](https://github.com/hadashiA/VContainer) | |
| 画面遷移 | [UnityScreenNavigator](https://github.com/chinpanGX/UnityScreenNavigator) | 自作のフォーク(元は[Haruma-K/UnityScreenNavigator](https://github.com/Haruma-K/UnityScreenNavigator))。Presenter起点の画面遷移・遷移の直列化を追加 |
| アセット管理 | [Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@latest) | |
| アドレスの管理 | [SmartAddresser](https://github.com/CyberAgentGameEntertainment/SmartAddresser) / [AddressDefinitionGenerator](https://github.com/CyberAgentGameEntertainment/AddressDefinitionGenerator) | ルールによるアドレスの一元管理と、アクセス用定数の自動生成 |
| 非同期 | [UniTask](https://github.com/Cysharp/UniTask) | |
| リアクティブ | [R3](https://github.com/Cysharp/R3) | View→Presenterの通知 |
| メッセージング | [ZeroMessenger](https://github.com/annulusgames/ZeroMessenger) | 画面をまたぐ通知(Pub/Sub)。直接は使わず、Supplementの`IMessageBroker`([Supplement.ZeroMessenger](https://github.com/chinpanGX/Supplement/tree/main/Assets/Supplement.ZeroMessenger))経由で使う |
| コレクション操作 | [ZLinq](https://github.com/Cysharp/ZLinq) | アロケーションの少ないLINQ |
| 演出 | [LitMotion](https://github.com/annulusgames/LitMotion) | Tween(HPゲージが減る演出など) |
| マスターデータ | [MasterMemory](https://github.com/Cysharp/MasterMemory) | [master-data-pipeline](https://github.com/chinpanGX/master-data-pipeline)(自作ツール)が生成するマスターデータのランタイム |
| シリアライズ | [MessagePack for C#](https://github.com/MessagePack-CSharp/MessagePack-CSharp) | マスターデータとMagicOnionの通信で使用 |
| リアルタイム通信 | [MagicOnion](https://github.com/Cysharp/MagicOnion) / [YetAnotherHttpHandler](https://github.com/Cysharp/YetAnotherHttpHandler) | バトルサーバーとの通信(gRPC/StreamingHub)。HTTP/2はIL2CPPでも同じ動作になるネイティブ実装を使う |
| REST通信 | [api-codegen](https://github.com/chinpanGX/api-codegen)の生成コード | `UnityWebRequest` + `UniTask`。APIサーバーのOpenAPI仕様書から生成(自作ツール) |
| 共通ユーティリティ | [Supplement](https://github.com/chinpanGX/Supplement) / [Supplement.ZeroMessenger](https://github.com/chinpanGX/Supplement/tree/main/Assets/Supplement.ZeroMessenger) | 自作。暗号化・永続化・アセットの読み込み・`IMessageBroker`等 |
| パッケージ管理 | [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) | NuGetパッケージのUnity導入 |
| 開発支援 | [Unity CLI Loop](https://github.com/hatayama/unity-cli-loop) | Claude CodeなどのAIエージェントからUnity Editorを操作する |

### Server

**言語・ランタイム**

- [Rust](https://www.rust-lang.org/)(edition 2024)
- 非同期ランタイム:[tokio](https://github.com/tokio-rs/tokio)

**Webフレームワーク**

- [axum](https://github.com/tokio-rs/axum)

**データベース**

- [MySQL](https://www.mysql.com/)
- DBライブラリ:[sqlx](https://github.com/launchbadge/sqlx)(生SQL。実行時に組み立てる`sqlx::query`を使う)

**インフラ・実行環境**

- [Docker](https://www.docker.com/) / [Docker Compose](https://docs.docker.com/compose/)(MySQLコンテナのローカル構築)
- 環境変数管理:[dotenvy](https://github.com/allan2/dotenvy)(`.env`でDB接続情報を管理)

**API仕様**

- [utoipa](https://github.com/juhaku/utoipa)(ハンドラの注釈からOpenAPI仕様書を生成し、`api-codegen`でUnity向けのコードを生成する)

**アーキテクチャ**

- レイヤードアーキテクチャ(`api` / `service` / `model` の3層構造 + 横断的な`extractor`)
  - `api`:リクエスト/レスポンスの型定義とハンドラ
  - `service`:認証処理・DB操作などのコアロジック
  - `model`:DBテーブルに対応するデータ構造
  - `extractor`:要認証エンドポイント共通の認証チェック(axumの`FromRequestParts`)

**テスト**

- 標準テスト機構(`cargo test`)
- DB込みの統合テスト:`sqlx::test`
- APIレベルの結合テスト:`tower::ServiceExt::oneshot`([tower](https://github.com/tower-rs/tower))

### BattleServer

**言語・ランタイム**

- [C#](https://learn.microsoft.com/dotnet/csharp/)([.NET 10](https://dotnet.microsoft.com/))/ [ASP.NET Core](https://github.com/dotnet/aspnetcore)

**通信**

- [MagicOnion.Server](https://github.com/Cysharp/MagicOnion)(gRPC/StreamingHub。開発中はHTTP/2・非TLS)
- [Microsoft.IdentityModel.JsonWebTokens](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet)(APIサーバーが発行した`battle_token`の検証)

**テスト**

- [xUnit](https://github.com/xunit/xunit) + [Microsoft.AspNetCore.Mvc.Testing](https://learn.microsoft.com/aspnet/core/test/integration-tests)(サーバーをプロセス内で起動し、MagicOnionのクライアントで接続する結合テスト)
- 対戦相手ボット(`BattleBot`、RESTとMagicOnionで自動対戦する)
