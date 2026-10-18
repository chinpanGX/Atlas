---
title: "利用技術"
status: current
area: root
related: [overview, server/structure, battle-server/structure, master-data/pipeline]
code: [Client/AtlasUnityProject/Packages/manifest.json, Client/AtlasUnityProject/Assets/Packages, Server/Cargo.toml, BattleServer]
updated: 2026-10-18
---

# 利用技術

Atlas で使っている言語・ライブラリ・ツールと、そのライセンス。ライセンスは各パッケージの `package.json`・`.nuspec`・`Cargo.toml`
とリポジトリの LICENSE で確かめた。ライブラリのバージョンは書かない(正は各プロジェクトの依存の定義)。

## 全体

| 層 | 役割 | 言語・ランタイム | 主な技術 |
|---|---|---|---|
| クライアント | 画面と操作、対戦の演出 | Unity 6.6(6000.6.5f1)、C#。IL2CPP で Windows 向けにビルド | VContainer(DI)、UniTask(非同期)、R3(イベントの通知)、UnityScreenNavigator(画面遷移。フォーク) |
| APIサーバー | 認証・所持データ・スカウト・ショップ・マッチングなど、対戦の進行以外のすべて。DB に書き込む唯一のサーバー | Rust(edition 2024) | tonic(gRPC)、sqlx(MySQL)、VolTech gRPC(自作の通信基盤。認証・再送の扱い・トランザクション) |
| バトルサーバー | リアルタイム対戦の進行と判定 | C#、.NET 10(ASP.NET Core) | MagicOnion(リアルタイムの双方向通信)、`Atlas.BattleCore`(Unity と共有するダメージ計算・ターン処理) |
| DB | プレイヤーの所持データと対戦の記録 | MySQL 8.4(Docker のコンテナ) | |

## 自作

ほかのプロジェクトでも使えるよう、Atlas の外のリポジトリにしたもの。足りないものは Atlas の中で包まず、それぞれのリポジトリで直す。

| 名前 | 用途 | ライセンス | 備考 |
|---|---|---|---|
| [VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC) | Client と APIサーバーの通信の基盤 | MIT | デバイス認証・エラーのダイアログ・再送しても一度だけ処理する仕組み・1リクエスト1トランザクションを受け持つ。Rust のクレートと Unity のパッケージをタグで参照する |
| [Supplement](https://github.com/chinpanGX/Supplement) | Unity の共通ユーティリティ | MIT | アセットのロード機能・メッセージング・ローカル保存機能など |
| [master-data-pipeline](https://github.com/chinpanGX/master-data-pipeline) | マスターデータの生成 | MIT | 1つのマスターデータから、Unity・C#サーバー・Rustサーバー向けの型とデータを生成して配る。submodule |
| [UnityScreenNavigator(フォーク)](https://github.com/chinpanGX/UnityScreenNavigator) | 画面遷移 | MIT | 元は Haruki Yano 氏の USN。Presenter 起点の遷移(VContainer 連携)・Overlay・遷移の直列化を足している。submodule |

## クライアント(Unity)

Unity 公式のパッケージ以外に使っているもの。導入は、どこから入れているか(`Git` は `manifest.json` の Git URL、`NuGet` は NuGetForUnity)。

| 名前 | 用途 | ライセンス | 導入 | 備考 |
|---|---|---|---|---|
| [VContainer](https://github.com/hadashiA/VContainer) | DI | MIT | OpenUPM | |
| [UniTask](https://github.com/Cysharp/UniTask) | 非同期 | MIT | OpenUPM | |
| [R3](https://github.com/Cysharp/R3) | リアクティブ(View → Presenter の通知) | MIT | Git・NuGet | |
| [LitMotion](https://github.com/annulusgames/LitMotion) | UI・対戦の演出のアニメーション | MIT | Git | |
| [ZLinq](https://github.com/Cysharp/ZLinq) | アロケーションの少ない LINQ | MIT | NuGet | Drop-in Generator で LINQ を置き換える |
| [MagicOnion](https://github.com/Cysharp/MagicOnion)(Client) | 対戦の通信(StreamingHub) | MIT | OpenUPM・NuGet | |
| [YetAnotherHttpHandler](https://github.com/Cysharp/YetAnotherHttpHandler) | HTTP/2 の通信 | MIT | OpenUPM | IL2CPP で標準の HTTP/2 が不安定なため使う |
| [Grpc.Net.Client](https://github.com/grpc/grpc-dotnet) | gRPC のクライアント | Apache-2.0 | UnityNuGet | |
| [Google.Protobuf](https://github.com/protocolbuffers/protobuf) | Protocol Buffers の実行時ライブラリ | BSD-3-Clause | UnityNuGet | |
| [MessagePack for C#](https://github.com/MessagePack-CSharp/MessagePack-CSharp) | シリアライズ(マスターデータ・対戦の通信・セーブデータ) | MIT | NuGet | IL2CPP で使えるよう、Source Generator を含む NuGet 版を入れる(MagicOnion・MasterMemory も同じ) |
| [MasterMemory](https://github.com/Cysharp/MasterMemory) | マスターデータの読み込みと検索 | MIT | NuGet | |
| [SmartAddresser](https://github.com/CyberAgentGameEntertainment/SmartAddresser) | Addressables のアドレスをルールで管理 | MIT | Git | |
| [AddressDefinitionGenerator](https://github.com/CyberAgentGameEntertainment/AddressDefinitionGenerator) | アドレスの定数(`AddressDefinition.cs`)の生成 | MIT | Git | |
| [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) | NuGet のパッケージの導入 | MIT | Git | |

## APIサーバー(Rust)

| 名前 | 用途 | ライセンス | 備考 |
|---|---|---|---|
| [tonic](https://github.com/hyperium/tonic) | gRPC のサーバー | MIT | クライアント向けと内部 API を別のポートで出す |
| [prost](https://github.com/tokio-rs/prost) | Protocol Buffers のコード生成 | Apache-2.0 | `build.rs` で生成する |
| [tokio](https://github.com/tokio-rs/tokio) | 非同期ランタイム | MIT | |
| [tower](https://github.com/tower-rs/tower) | gRPC のサービスの Layer(認証・日付変更) | MIT | |
| [sqlx](https://github.com/launchbadge/sqlx) | MySQL へのアクセス、マイグレーション | MIT OR Apache-2.0 | 生 SQL。`query!` マクロ(コンパイル時に DB が要る)は使わず、実行時に組み立てる `sqlx::query` を使う |
| [jsonwebtoken](https://github.com/Keats/jsonwebtoken) | `battle_token`(JWT)の発行 | MIT | |
| [subtle](https://github.com/dalek-cryptography/subtle) | 内部 API のシークレットの定数時間での比較 | BSD-3-Clause | |
| [ulid](https://github.com/dylanhart/ulid-rs) | プレイヤーの操作で作るデータの ID | MIT | |
| [serde](https://github.com/serde-rs/serde) | 構造体のシリアライズ | MIT OR Apache-2.0 | |
| [serde_json](https://github.com/serde-rs/json) | JSON の列の読み書き | MIT OR Apache-2.0 | |
| [chrono](https://github.com/chronotope/chrono) | 日時 | MIT OR Apache-2.0 | |
| [rand](https://github.com/rust-random/rand) | スカウトの抽選・ボットのパーティ | MIT OR Apache-2.0 | |
| [tracing](https://github.com/tokio-rs/tracing) | 構造化ログ | MIT | 出力の準備は VolTech gRPC が行う |
| [dotenvy](https://github.com/allan2/dotenvy) | `.env` の読み込み | MIT | |

## バトルサーバー(C#)

| 名前 | 用途 | ライセンス | 備考 |
|---|---|---|---|
| [MagicOnion](https://github.com/Cysharp/MagicOnion)(Server) | 対戦の通信(StreamingHub) | MIT | |
| [Grpc.Net.Client](https://github.com/grpc/grpc-dotnet) | APIサーバーの内部 API の呼び出し | Apache-2.0 | |
| [Grpc.Tools](https://github.com/grpc/grpc) | 内部 API のクライアントのコード生成 | Apache-2.0 | `.proto` からビルド時に生成する |
| [MessagePack for C#](https://github.com/MessagePack-CSharp/MessagePack-CSharp) | シリアライズ(マスターデータ・対戦の通信) | MIT | |
| [MasterMemory](https://github.com/Cysharp/MasterMemory) | マスターデータの読み込みと検索 | MIT | クライアントと同じ `masterdata.bytes` を読む |
| Microsoft.IdentityModel.JsonWebTokens | `battle_token` の検証 | MIT | |
| [xUnit](https://github.com/xunit/xunit) | バトルサーバーのテスト | Apache-2.0 | |
| [NUnit](https://github.com/nunit/nunit) | `Atlas.BattleCore` のテスト | MIT | `dotnet test` で回す |

## 共通・インフラ

| 名前 | 用途 | ライセンス | 備考 |
|---|---|---|---|
| Protocol Buffers | APIサーバーの通信の定義(`Shared/proto/`) | BSD-3-Clause | |
| MySQL | データベース | GPL-2.0(Community Edition) | |
| Docker | 開発環境のコンテナ(MySQL・サーバー) | Docker Engine は Apache-2.0。Docker Desktop は Docker の利用規約 | |
| [GameCI](https://game.ci/) | CI(GitHub Actions)での Unity のビルド | MIT | |
