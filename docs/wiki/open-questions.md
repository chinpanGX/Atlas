---
title: "未確定・保留"
status: open
area: root
related: [game/party-training, game/shop, client/error-dialog, battle-server/battle-flow, master-data/pipeline]
updated: 2026-10-12
---

# 未確定・保留

決まったら、ここから消して該当ページに反映する。

## 仕様

| 論点 | 決まっていること | 関係するページ |
|---|---|---|
| ステータスポイントの割り振り・育成チケット・ショップの API 設計 | 技の付け替えはチケット5枚、割り振りは増やしたポイント1つにつき1枚。64ポイントを6能力に上限なしで割り振り、能力の値にそのまま足す(今の `floor(努力値 / 4)` から計算式を変える)。ショップは販売期間・購入履歴なし | [パーティ編成・育成](game/party-training.md)、[ショップ](game/shop.md)、[バトルの計算](battle-server/battle-core.md) |
| 1つの商品を一度に複数個買えるか、購入数の上限(1日あたり・所持数) | チケットは10枚・100枚・200枚の商品に分けてある | [ショップ](game/shop.md) |
| 報酬額(初期300・勝利50)とスカウト・育成チケットの値段のバランス | いまの値は仮 | [ジェム](game/gems.md) |
| 技の付け替え・ニックネーム入力の画面 | API はある(ニックネームはサインアップの引数) | [パーティ編成・育成](game/party-training.md)、[サインイン](client/sign-in.md) |

## 設計

| 論点 | 関係するページ |
|---|---|
| 通信中の Loading をすべての通信で自動にするか。捕まえ漏れの例外の通知 | [通信エラーのダイアログ](client/error-dialog.md) |
| Client の再接続と、強制交代ターンの途中で再接続したときの状態の伝え方(`OnMatchStart` に強制交代の状態が無い) | [対戦の進行](battle-server/battle-flow.md) |
| 対戦結果(勝敗・報酬)を Home へ戻ったときにどう見せるか | [バトル画面](client/battle-screen.md) |
| マスターデータの Client への配布を CDN 経由にするか(バージョン管理・差分検知を決めていない) | [マスターデータの生成・配布・運用](master-data/pipeline.md) |
| 能力の計算式が Client(`PachimonStatCalculator`)とバトルサーバー(`LoadoutBuilder`)の2か所にある | [バトルの計算](battle-server/battle-core.md) |
| 接続先(本番・ステージング等)の切り替え | [サインイン](client/sign-in.md) |
| デプロイの方式(Docker 化、TLS、HTTP/2 のみの Kestrel の設定の見直し) | [バトルサーバーの構成](battle-server/structure.md) |
