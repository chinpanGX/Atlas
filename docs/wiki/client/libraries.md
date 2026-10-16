---
title: "採用ライブラリ"
status: current
area: client
related: [client/assemblies, client/navigation, client/services-and-player-data]
code: [Client/AtlasUnityProject/Packages/manifest.json, Client/AtlasUnityProject/Assets/packages.config]
updated: 2026-10-11
---

# 採用ライブラリ

ビルドは IL2CPP を前提にする。導入経路はライブラリごとに異なる。

| 用途 | ライブラリ | 導入経路 |
|---|---|---|
| DI | VContainer | OpenUPM |
| 非同期 | UniTask | OpenUPM |
| リアクティブ(View → Presenter の通知) | R3 | Git + NuGetForUnity |
| UI のアニメーション | LitMotion | Git |
| LINQ(アロケーションの少ない置き換え) | ZLinq(Drop-in Generator) | NuGetForUnity |
| アセット管理 | Addressables | Unity |
| アドレスの管理 | SmartAddresser(ルールでアドレスを一元管理)・AddressDefinitionGenerator(アクセス用の定数 `AddressDefinition.cs` を生成) | Git |
| 画面遷移 | UnityScreenNavigator(USN)のフォーク | submodule(`file:`) |
| 共通ユーティリティ(ローダー、`IMessageBroker` 等) | Supplement(自作) | Git(タグ) |
| セーブデータ(アカウント情報) | Supplement.MessagePack(自作。MessagePack + LZ4 を AES で暗号化し、HMAC で改ざんを検知) | Git(タグ) |
| 画面をまたぐ通知 | ZeroMessenger(Supplement の `IMessageBroker` 経由でだけ使う) | NuGetForUnity |
| マスターデータ | MasterMemory + MessagePack | NuGetForUnity |
| シリアライズ(マスターデータ・対戦の通信・セーブデータ) | MessagePack(本体と Source Generator `MessagePackAnalyzer`) | NuGetForUnity |
| 対戦の通信 | MagicOnion.Client + YetAnotherHttpHandler + Grpc.Net.Client | OpenUPM + NuGetForUnity + UnityNuGet |
| APIサーバーとの通信 | VolTech gRPC(自作の基盤。`ApiCore`・認証・エラーダイアログの仕組み・gRPC Monitor)+ `.proto` から生成したラッパー | Git(タグ `v0.3.0`)+ 生成コード |
| テスト | Unity Test Framework | Unity |
| NuGet パッケージの導入 | NuGetForUnity | Git |
| 開発支援(AI エージェントから Editor を操作する) | Unity CLI Loop(`uloop`) | Git |

- MagicOnion・MasterMemory・MessagePack は、UPM 版が薄いラッパーで Source Generator を含まないため、NuGet 版(本体 + Source Generator)を
  NuGetForUnity で入れている。IL2CPP では動的なコード生成が使えず、`[MessagePackObject]` の型のフォーマッターを Source Generator で作る必要があるため
- APIサーバーとの通信と対戦の通信は、同じ Grpc.Net.Client と YetAnotherHttpHandler を使う
- 標準の `SocketsHttpHandler` ではなく YetAnotherHttpHandler を使う。IL2CPP で標準の HTTP/2(ALPN)が不安定なため、Editor・IL2CPP とも
  同じネイティブの HTTP/2 実装にそろえる
- 画面遷移は、Push/Pop のスタック型で足りる規模のため、フロー制御(名前付きルート等)を持つ自作のフレームワークは作らず USN を使う。
  フォーク([chinpanGX/UnityScreenNavigator](https://github.com/chinpanGX/UnityScreenNavigator)、`develop` ブランチ)で、
  Presenter 起点の画面遷移(VContainer 連携)・Overlay・遷移の直列化を足している
- ZLinq は `ZLinq.Bridge` アセンブリに `ZLinqDropIn` 属性で拡張メソッドを public に生成し、それを参照したアセンブリの LINQ を ZLinq に置き換える
- スクリプト定義シンボル `USE_VCONTAINER`(Supplement の VContainer 連携)と `USN_USE_ADDRESSABLES`(USN の Addressables ローダー)を有効にしている
