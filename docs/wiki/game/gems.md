---
title: "ジェム"
status: current
area: game
related: [conventions/items, game/scout, game/shop, open-questions]
code: [Server/src/service/player_service.rs, Server/src/service/battle_service.rs, Server/src/grpc/debug.rs]
updated: 2026-10-18
---

# ジェム

| 増減 | きっかけ | 量 |
|---|---|---|
| 増える | プレイヤー作成時 | 300 |
| 増える | 対戦の勝利 | 50 |
| 増える | 開発用の付与ボタン(正式な機能ではない) | 450 |
| 減る | スカウトの紹介1回 | 150 |
| 減る | ショップでの購入(育成チケット10枚・100枚・200枚) | 50・450・800 |

- 所持数の増減はすべてサーバーが決める
- 報酬額とスカウト・育成チケットの値段のバランスは仮の値([未確定・保留](../open-questions.md))

作りは [所持リソース(アイテム)](../conventions/items.md)。
