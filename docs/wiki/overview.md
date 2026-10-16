---
title: "概要(ゲームとシステムの全体像)"
status: current
area: root
related: [game/index, conventions/index, server/structure, client/index, battle-server/index]
proto: [Shared/proto/atlas/v1, Shared/proto/atlas/internal/v1/battle.proto]
code: [Server/src/main.rs, Server/src/grpc.rs, BattleServer/Program.cs, Client/AtlasUnityProject/Assets/Scripts/DI/RootLifetimeScope.cs]
updated: 2026-10-13
---

# 概要(ゲームとシステムの全体像)

## ゲーム

「パチモン」と呼ぶモンスターを集めてパーティを組み、ほかのプレイヤーとリアルタイムで1対1の対戦をするゲーム。
会員登録は無く、初回起動時に端末ごとのゲストアカウントで始まる。本家ポケモンの対戦ルールから、天候・フィールド・持ち物・
特性・技の追加効果などを外した、小さなルールで遊ぶ。ルールの詳細は [ゲームの仕様](game/index.md)。

## システム構成

```
Unity Client
   │ gRPC(VolTech gRPC)           │ MagicOnion (gRPC / StreamingHub)
   ▼                               ▼
APIサーバー(Rust/tonic)        バトルサーバー(C#/MagicOnion)
 認証・プレイヤー・編成・          対戦の進行のみ(状態はメモリ上)
 スカウト・マッチング・DB書き込み        │
   ▲──────── 内部API(gRPC、別ポート) ────┘  パーティ・選出個体の取得、結果報告
   │
 MySQL(唯一のデータストア。書き込みはAPIサーバーだけが行う)
```

| 決めたこと | 理由 |
|---|---|
| サーバーは1台構成(ローカルでの動作)。マッチングの待機列と対戦の状態はプロセスのメモリに持つ | 規模に対してスケールアウトの仕組みは過剰。DB や Redis に置く手間を省く |
| DB への書き込みは APIサーバーに一本化する。バトルサーバーは DB を持たず、内部 API で取得・報告する | DB のスキーマとトランザクションを知る場所を1つにする |
| 対戦の判定はバトルサーバーが権威を持つ。クライアントは結果を受け取って表示するだけ | 不正(ダメージの改ざんなど)を防ぐ。HP は%でしか送らない等、判定に使う生の値もクライアントに渡さない |
| バトルサーバーへの参加資格は、APIサーバーがマッチ成立時に発行する短命の JWT(`battle_token`)で確かめる | バトルサーバーがユーザーの DB を持たずに済む |
| ダメージ計算・ターン処理(`Atlas.BattleCore`)と対戦の通信契約(`Atlas.BattleContracts`)は、`Shared/` の同じソースを Client と BattleServer がコンパイルする | 計算と契約が両者で食い違わない。Client はオフラインの Mock 対戦にも同じ計算を使う |
| Client と APIサーバーの通信は、自作の gRPC 基盤 [VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC)(タグ `v0.3.0`)に乗せる | デバイス認証・トークン・エラーのダイアログ・再送しても一度だけ処理する仕組み・1リクエスト1トランザクション・メンテナンスの判定を基盤が受け持ち、Atlas は `.proto` と業務処理だけを書く |

### 通信の一覧

| 経路 | 方式 | 認証 | 用途 | 詳細 |
|---|---|---|---|---|
| Client → APIサーバー | gRPC(Unary)。`.proto` から生成したラッパーを基盤の `ApiCore` 経由で呼ぶ | `authorization: Bearer <access_token>`(基盤が付与・更新) | 認証・所持データ・編成・スカウト・マッチング・開発用 | [APIサーバーの構成](server/structure.md) |
| Client ⇔ バトルサーバー | MagicOnion StreamingHub(HTTP/2) | `battle_token`(JWT) | 対戦(選出・行動・結果の受信) | [通信契約](battle-server/contracts.md) |
| バトルサーバー → APIサーバー | gRPC(`atlas.internal.v1.BattleInternalService`)。クライアント向けとは別のポート | メタデータ `x-internal-secret` | パーティ・選出個体の取得、結果報告 | [内部API](server/internal-api.md) |

## リポジトリ構成

| パス | 内容 |
|---|---|
| `Client/AtlasUnityProject/` | Unity クライアント([クライアント](client/index.md)) |
| `Server/` | APIサーバー(Rust。クライアント向けも内部 API も gRPC/tonic)([APIサーバー](server/index.md)) |
| `BattleServer/` | バトルサーバー(ASP.NET Core + MagicOnion)。対戦相手ボット(`BattleBot/`)とテストを含む([バトルサーバー](battle-server/index.md)) |
| `Shared/BattleCore/` | ダメージ計算・ターン処理(`Atlas.BattleCore`) |
| `Shared/BattleContracts/` | Client ⇔ BattleServer の通信契約(`Atlas.BattleContracts`) |
| `Shared/master-data/` | マスターデータのスキーマと CSV(正本)([マスターデータ](master-data/index.md)) |
| `Shared/proto/` | APIサーバーの通信の定義(`atlas/v1` がクライアント向け、`atlas/internal/v1` がバトルサーバー向け。正本) |
| `Shared/errors.toml` | APIサーバーの業務エラー(domain `atlas`。正本)([業務エラー](conventions/errors.md)) |
| `master-data-pipeline/`(submodule) | マスターデータの生成ツール(自作) |
| `UnityScreenNavigator/`(submodule) | 画面遷移ライブラリ(USN)のフォーク |
| `docs/` | この wiki と、元になった仕様概要書・設計書・構想・メモ |

### 生成物とその正本

生成物は手で編集しない(再生成で上書きされる)。正本を変えたら生成し直す。

| 正本 | 生成物 | 生成方法 |
|---|---|---|
| `Shared/master-data/` | Rust の型・JSON、C# の型・`masterdata.bytes`(Client・BattleServer) | `master-data-pipeline` スキル。Rust 側はさらに `seed_master_data` で DB へ投入 |
| `Shared/proto/`・`Shared/errors.toml` | Rust のサーバーの型・エラーコード(`build.rs`。コミットしない)、Client の `Infrastructure/Grpc/Generated/`、BattleServer・BattleBot のクライアント(`Grpc.Tools`、ビルド時) | Rust・C#(.NET)はビルドで自動。Unity はルートで `make proto-gen`・`make errors-gen` |
| Addressables の登録 | Client の `AddressDefinition.cs` | Unity のジェネレーター |

起動手順・環境変数・コマンドは [開発環境ガイド](development.md)。
