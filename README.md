# Atlas

Unityクライアント、Rustの APIサーバー、C#/MagicOnion のバトルサーバーで作る、リアルタイム対戦のモンスター収集ゲーム。

![Atlas](docs/wiki/images/screenshots/hero.png)

> 仕様と設計は **[Atlas wiki](docs/wiki/index.md)** にまとめ、実装に合わせて更新している。
> 遊べる画面のスクリーンショットから入り、各層の設計とその理由までリンクでたどれる([wikiの中身](#ドキュメント))。

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

### 機能一覧

| 機能 | 概要 | 詳しく |
| --- | --- | --- |
| アカウント | 会員登録なしのデバイス認証、プレイヤー作成、所持データの同期 | [タイトルとアカウント](docs/wiki/features/account.md) |
| パーティ編成・育成 | パーティ編成、技の付け替え、ステータスポイントの割り振り | [パーティ編成・育成](docs/wiki/features/party-training.md) |
| スカウト(ガチャ) | ジェムを使い、候補10体から1体を選んで入手する | [スカウト](docs/wiki/features/scout.md) |
| ショップ | ジェムで育成チケットを購入する | [ショップ](docs/wiki/features/shop.md) |
| 対戦 | マッチング、パーティの見せ合いと選出、リアルタイム対戦、結果の記録 | [対戦](docs/wiki/features/battle.md) |

各ページに、画面のスクリーンショット・ルール・作りの要点(技術資料へのリンク)をまとめている。

## ドキュメント

仕様と設計は **[Atlas wiki](docs/wiki/index.md)** にまとめている。

- **機能から入れる**: 機能ごとのページに、スクリーンショット・ルール・作りの要点を置き、技術資料へリンクでたどれる
- **理由を残す**: 何をするかに加えて、なぜそうしたかと、検討して採用しなかった案を書いている
- **コードとつながっている**: 各ページの先頭に、そのページが説明するコード・`.proto` のパスを書いている。コードを変えたら、そのパスから直すページを探す
- **正本を写さない**: API の形は `.proto`、業務エラーは `Shared/errors.toml`、マスタの列はスキーマと CSV を正本にし、wiki には振る舞いと理由を書く

| エリア | 内容 |
| --- | --- |
| [機能](docs/wiki/features/index.md) | 画面ごとのスクリーンショット、ゲームのルール、作りの要点 |
| [概要](docs/wiki/overview.md) | システム構成、通信の一覧、技術の見どころ、リポジトリ構成 |
| [共通の決まり](docs/wiki/conventions/index.md) | 命名とID、値なしの表し方、所持データの差分(`player_diff`)、アイテム、業務エラー |
| [APIサーバー](docs/wiki/server/index.md) | 構成、認証、各 API の振る舞い、マッチング、ゲームログ |
| [DB](docs/wiki/db/index.md) | 方針と、全テーブルの列・つながり |
| [マスターデータ](docs/wiki/master-data/index.md) | 生成・配布・運用、各テーブルの列 |
| [クライアント](docs/wiki/client/index.md) | アセンブリとレイヤー、シーンとスコープ、画面の構成と遷移、所持データの反映、バトル画面 |
| [バトルサーバー](docs/wiki/battle-server/index.md) | 構成、バトルの計算、通信契約、対戦の進行、APIサーバーとの連携、テストとボット |
| [開発環境ガイド](docs/wiki/development.md) | 環境構築、起動手順、よく使うコマンド、トラブルシューティング |

読み方の例:

- 遊べるものを見る: [ゲーム紹介](docs/wiki/features/index.md) → 各機能のページ
- 設計の見どころを知る: [概要](docs/wiki/overview.md)の「技術の見どころ」
- 作りを詳しく見る: 技術資料(APIサーバー → DB → バトルサーバー → クライアント)
- 動かす: [開発環境ガイド](docs/wiki/development.md)

残タスクは [GitHub Issues](https://github.com/chinpanGX/Atlas/issues) で管理している。

## 技術スタック

| 層 | 技術 | 詳しく |
| --- | --- | --- |
| クライアント | Unity 6.6(IL2CPP)、VContainer、UniTask、R3、LitMotion、ZLinq、Addressables、UnityScreenNavigator(フォーク)、MasterMemory、MessagePack、MagicOnion.Client | [利用技術](docs/wiki/tech-stack.md)・[アセンブリとレイヤー](docs/wiki/client/assemblies.md) |
| APIサーバー | Rust、tonic(gRPC)、sqlx、MySQL、jsonwebtoken | [APIサーバーの構成](docs/wiki/server/structure.md) |
| バトルサーバー | C#(.NET 10)、ASP.NET Core、MagicOnion、xUnit | [バトルサーバーの構成](docs/wiki/battle-server/structure.md) |
| 共通 | Protocol Buffers、[VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC)(自作)、[Supplement](https://github.com/chinpanGX/Supplement)(自作)、[master-data-pipeline](https://github.com/chinpanGX/master-data-pipeline)(自作) | [概要](docs/wiki/overview.md)・[マスターデータの生成・配布・運用](docs/wiki/master-data/pipeline.md) |

使っているライブラリ・ツールの用途とライセンスは [利用技術](docs/wiki/tech-stack.md)。
