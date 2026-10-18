# Atlas

Unityクライアント、Rustの APIサーバー、C#/MagicOnion のバトルサーバーで作る、リアルタイム対戦のモンスター収集ゲーム。

![Atlas](docs/wiki/images/screenshots/hero.png)

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

| 機能 | 概要 | 詳しく |
| --- | --- | --- |
| アカウント | 会員登録なしのデバイス認証、プレイヤー作成、所持データの同期 | [タイトルとアカウント](docs/wiki/features/account.md) |
| パーティ編成・育成 | パーティ編成、技の付け替え、ステータスポイントの割り振り | [パーティ編成・育成](docs/wiki/features/party-training.md) |
| スカウト(ガチャ) | ジェムを使い、候補10体から1体を選んで入手する | [スカウト](docs/wiki/features/scout.md) |
| ショップ | ジェムで育成チケットを購入する | [ショップ](docs/wiki/features/shop.md) |
| 対戦 | マッチング、パーティの見せ合いと選出、リアルタイム対戦、結果の記録 | [対戦](docs/wiki/features/battle.md) |

各ページに、画面のスクリーンショット・ルール・作りの要点(技術資料へのリンク)をまとめている。

## 技術スタック

| 層 | 技術 | 詳しく |
| --- | --- | --- |
| クライアント | Unity 6.6(IL2CPP)、VContainer、UniTask、R3、LitMotion、ZLinq、Addressables、UnityScreenNavigator(フォーク)、MasterMemory、MessagePack、MagicOnion.Client | [利用技術](docs/wiki/tech-stack.md)・[アセンブリとレイヤー](docs/wiki/client/assemblies.md) |
| APIサーバー | Rust、tonic(gRPC)、sqlx、MySQL、jsonwebtoken | [APIサーバーの構成](docs/wiki/server/structure.md) |
| バトルサーバー | C#(.NET 10)、ASP.NET Core、MagicOnion、xUnit | [バトルサーバーの構成](docs/wiki/battle-server/structure.md) |
| 共通 | Protocol Buffers、[VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC)(自作)、[Supplement](https://github.com/chinpanGX/Supplement)(自作)、[master-data-pipeline](https://github.com/chinpanGX/master-data-pipeline)(自作) | [概要](docs/wiki/overview.md)・[マスターデータの生成・配布・運用](docs/wiki/master-data/pipeline.md) |

使っているライブラリ・ツールの用途とライセンスは [利用技術](docs/wiki/tech-stack.md)。

## 読む場所

| 知りたいこと | 読む場所 |
| --- | --- |
| システム構成・通信の経路・リポジトリ構成 | [概要](docs/wiki/overview.md) |
| 設計の見どころ | [概要](docs/wiki/overview.md)の「技術の見どころ」 |
| 仕様と設計(実装に合わせて更新) | [Atlas wiki](docs/wiki/index.md) |
| 環境構築・起動手順 | [開発環境ガイド](docs/wiki/development.md) |
| 残タスク | [GitHub Issues](https://github.com/chinpanGX/Atlas/issues) |
