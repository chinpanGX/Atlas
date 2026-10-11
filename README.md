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

仕様と設計は **[Atlas wiki](docs/wiki/index.md)** にまとめている(実装に合わせて更新)。

### 機能一覧

| 機能 | 概要 | 仕様 | 設計 |
| --- | --- | --- | --- |
| アカウント | 会員登録なしのデバイス認証、プレイヤー作成、所持データの同期 | [遊びの流れとアカウント](docs/wiki/game/flow.md) | [認証](docs/wiki/server/auth.md)・[サインイン](docs/wiki/client/sign-in.md) |
| パーティ編成・育成 | パーティ編成、技の付け替え、ステータスポイントの割り振り(追加予定) | [パーティ編成・育成](docs/wiki/game/party-training.md) | [パーティ編成・技の付け替え](docs/wiki/server/party.md) |
| スカウト(ガチャ) | ジェムを使い、候補10体から1体を選んで入手する | [スカウト](docs/wiki/game/scout.md) | [スカウト(API)](docs/wiki/server/scout.md) |
| ショップ | ジェムで育成チケットを購入する(追加予定) | [ショップ](docs/wiki/game/shop.md) | — |
| 対戦 | マッチング、パーティの見せ合いと選出、リアルタイム対戦、結果の記録 | [対戦のルール](docs/wiki/game/battle-rules.md) | [マッチング](docs/wiki/server/matchmaking.md)・[対戦の進行](docs/wiki/battle-server/battle-flow.md) |

### システム構成(概要)

```
Unity Client
   │ gRPC(VolTech gRPC)           │ MagicOnion (gRPC / StreamingHub)
   ▼                               ▼
APIサーバー(Rust/tonic)       バトルサーバー(C#/MagicOnion)
 認証・プレイヤー・編成・          対戦の進行のみ(状態はメモリ上)
 スカウト・マッチング・DB書き込み        │
   ▲──────── 内部API(gRPC、別ポート) ────┘  パーティ・選出個体の取得、結果報告
   │
 MySQL(唯一のデータストア。書き込みはAPIサーバーだけが行う)
```

## 構成

```
Atlas/
├── Client/AtlasUnityProject/  # Unityクライアント
├── Server/                    # APIサーバー(Rust。クライアント向けも内部APIもgRPC/tonic)
├── BattleServer/              # バトルサーバー(C#/MagicOnion)、対戦相手ボット、テスト
├── Shared/                    # Client・サーバーで共有するもの
│   ├── BattleCore/            # ダメージ計算・ターン処理(ClientとBattleServerが同じソースを使う)
│   ├── BattleContracts/       # Client⇔BattleServerの通信契約
│   ├── master-data/           # マスターデータのスキーマとCSV(正本)
│   ├── proto/                 # ClientとAPIサーバーの通信の定義(.proto、正本)
│   └── errors.toml            # APIサーバーの業務エラー(正本)
├── docs/                      # wiki(docs/wiki/)、仕様概要書・設計書・進捗・Notesなど
├── master-data-pipeline/      # マスターデータの定義・自動生成ツール(submodule、自作)
└── UnityScreenNavigator/      # 画面遷移ライブラリ(USN)のフォーク(submodule)
```

実装の進み具合は[docs/progress.md](docs/progress.md)を参照。

開発環境の構築・起動手順は[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)を参照。

ドキュメント:

- [docs/wiki/index.md](docs/wiki/index.md) — **Atlas wiki**(仕様と設計。実装に合わせて更新している。まずはここから)
- [docs/game-spec.md](docs/game-spec.md) — 仕様概要書(ゲームの内容とルール)
- [docs/design.md](docs/design.md) — 設計書(APIサーバー・クライアント・バトルサーバー・DB・マスターデータ)
- [docs/progress.md](docs/progress.md) — 実装の進み具合と残タスク

## 設計上のポイント

- **自作のgRPC基盤**: ClientとAPIサーバーの通信は、自作のgRPC基盤[VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC)に乗せる。会員登録なしのデバイス認証・トークンの発行と再認証・エラーのダイアログ・再送しても一度だけ処理する仕組み(RequestID)・1リクエスト1トランザクション・メンテナンスの判定は基盤が受け持ち、Atlasは`.proto`・業務処理・ダイアログの見た目を持つ
- **DBへの書き込みの一本化**: MySQLへ書き込むのはAPIサーバーだけ。バトルサーバーはDBを持たず、パーティ・選出個体の取得と結果報告を内部API(gRPC。`x-internal-secret`で認証)で行う。対戦の判定はバトルサーバーが権威を持ち、クライアントにはHPを%でしか送らない
- **共通モジュール**: ダメージ計算・ターン処理(`Atlas.BattleCore`)と通信契約(`Atlas.BattleContracts`)は、同じソースをUnity(ローカルパッケージ)とバトルサーバー(csproj)の両方でコンパイルする
- **マスターデータ管理**: 正本は`Shared/master-data/`のスキーマとCSV(Googleスプレッドシートからも作れる)。`master-data-pipeline`がRust・C#向けの型とデータを生成し、APIサーバーはDBへ投入したものを起動時に全件読み込んでメモリに持つ。サーバー1台構成の規模に対して、無停止での反映は現時点で導入しない判断とした(詳細は[マスターデータの生成・配布・運用](docs/wiki/master-data/pipeline.md))
- **コード生成ツールの自作**: [master-data-pipeline](https://github.com/chinpanGX/master-data-pipeline)(マスターデータの定義・自動生成)は、特定プロジェクトに依存しない汎用ツールとして自作した(詳細はツールの`README.md`を参照)。ClientとAPIサーバーの通信のコードは、`.proto`からgRPC基盤の生成ツールで作る

## 技術スタック

### Client

- [Unity 6.6](https://unity.com/releases/unity-6)(6000.6.5f1)

**アーキテクチャ**

オニオンアーキテクチャを採用し、レイヤーをasmdefで分けて依存の向きをコンパイルで強制する。

```text
外側  DI                               構成ルート。全レイヤーの実装を組み立てる
       ↓
      Presentation / Infrastructure    View・Presenter / API通信・Mock・MagicOnion・端末への保存
       ↓
      Application                      Service(通信を含む)のインターフェース
       ↓
中心  Domain                           Entity、Repository(所持データ)のインターフェース
```

(矢印は依存の向き。外側のレイヤーは、それより内側のすべてのレイヤーを参照できる)

- 依存は外側から内側への一方向。Domainはどのレイヤーにも依存せず、PresentationとInfrastructureは互いを参照しない。
  通信や保存のインターフェースを内側(Application・Domain)に置き、外側のInfrastructureが実装する
- 各レイヤーの実装を組み立てるのは最も外側のDI(VContainerのLifetimeScope)だけ
- 画面はView / Presenterに分け、Presenterが具象のView(`Page`/`Modal`)をコンストラクタで受け取り、ViewのObservableを購読する
  (Viewにインターフェースは作らない)。Presenterは画面を開くたびに作る子スコープで生成し、閉じると破棄する
- 通信と手元のデータを分ける: 画面から使う処理とサーバーAPIの呼び出しは`Service`、所持データは`Repository`(メモリに保持)。
  所持データを変えるAPIの差分(`player_diff`)は、gRPC基盤の`ApiCore`がレスポンスを返す前にRepositoryへ反映する
- テストでは通信の送り先だけを偽のサーバーに差し替える(対戦は、クライアント内で完結するMock対戦と実サーバーをInspectorで切り替え)
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
| APIサーバーとの通信 | [VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC) | 自作のgRPC基盤。`.proto`から生成したラッパーと、認証・エラーダイアログの仕組み・再送しても一度だけ処理する仕組み・通信の記録(gRPC Monitor)を受け持つ`ApiCore` |
| 共通ユーティリティ | [Supplement](https://github.com/chinpanGX/Supplement) / [Supplement.ZeroMessenger](https://github.com/chinpanGX/Supplement/tree/main/Assets/Supplement.ZeroMessenger) | 自作。暗号化・永続化・アセットの読み込み・`IMessageBroker`等 |
| パッケージ管理 | [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) | NuGetパッケージのUnity導入 |
| 開発支援 | [Unity CLI Loop](https://github.com/hatayama/unity-cli-loop) | Claude CodeなどのAIエージェントからUnity Editorを操作する |

### Server

**言語・ランタイム**

- [Rust](https://www.rust-lang.org/)(edition 2024)
- 非同期ランタイム:[tokio](https://github.com/tokio-rs/tokio)

**通信**

- [tonic](https://github.com/hyperium/tonic) + [VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC)(自作の基盤のクレート。認証・冪等性・トランザクション・メンテナンスの判定)。クライアント向けのAPI
- tonic(BattleServer向けの内部API。別ポートで、基盤のLayerを付けずに共有シークレットで認証)

**データベース**

- [MySQL](https://www.mysql.com/)
- DBライブラリ:[sqlx](https://github.com/launchbadge/sqlx)(生SQL。実行時に組み立てる`sqlx::query`を使う)

**インフラ・実行環境**

- [Docker](https://www.docker.com/) / [Docker Compose](https://docs.docker.com/compose/)(MySQLコンテナのローカル構築)
- 環境変数管理:[dotenvy](https://github.com/allan2/dotenvy)(`.env`でDB接続情報を管理)

**API仕様**

- `.proto`(`Shared/proto/`)が唯一の定義。サーバーは`build.rs`で、Unity向けのC#は基盤のprotocプラグインで生成する

**アーキテクチャ**

- レイヤードアーキテクチャ(`grpc` / `service` / `model` の3層構造)
  - `grpc`:gRPCのサービスの実装(ハンドラ。クライアント向けと内部API)。業務処理を基盤のトランザクションの中で動かす
  - `service`:DB操作などのコアロジック
  - `model`:DBテーブルに対応するデータ構造

**テスト**

- 標準テスト機構(`cargo test`)
- DB込みの統合テスト:`sqlx::test`
- APIレベルの結合テスト:テストごとにgRPCサーバーを起動し、tonicのクライアントで呼ぶ

### BattleServer

**言語・ランタイム**

- [C#](https://learn.microsoft.com/dotnet/csharp/)([.NET 10](https://dotnet.microsoft.com/))/ [ASP.NET Core](https://github.com/dotnet/aspnetcore)

**通信**

- [MagicOnion.Server](https://github.com/Cysharp/MagicOnion)(gRPC/StreamingHub。開発中はHTTP/2・非TLS)
- [Microsoft.IdentityModel.JsonWebTokens](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet)(APIサーバーが発行した`battle_token`の検証)

**テスト**

- [xUnit](https://github.com/xunit/xunit) + [Microsoft.AspNetCore.Mvc.Testing](https://learn.microsoft.com/aspnet/core/test/integration-tests)(サーバーをプロセス内で起動し、MagicOnionのクライアントで接続する結合テスト)
- 対戦相手ボット(`BattleBot`、APIサーバーとMagicOnionで自動対戦する。gRPCへの乗せ換え中は動かない)
