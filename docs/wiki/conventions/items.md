---
title: "所持リソース(アイテム)"
status: current
area: conventions
related: [game/gems, db/player-tables, master-data/tables, conventions/player-diff]
code: [Server/src/service/player_item_service.rs, Shared/master-data/csv/items_master.csv]
updated: 2026-10-11
---

# 所持リソース(アイテム)

ジェムのような数量で持つリソースは、`players` に専用の列を持たせず、マスタ `items` と所持数テーブル `player_items` で管理する。

- 所持数は絶対値で、増減はすべてサーバーが決める
- 新しい通貨や消費アイテムを足すときに、テーブルや API の形を変えずに済む。[所持データの差分](player-diff.md) でも
  `items` の1行として同じように扱える
- 所持数が0になっても行は消さない

| `item_id` | アイテム | 状態 |
|---|---|---|
| 1 | ジェム | 使っている(サーバーの定数 `GEM_ITEM_ID`) |
| 2 | 育成チケット | マスタだけ。増減の処理は追加予定([ショップ](../game/shop.md)) |

## 増減する場所

| きっかけ | 増減 | 処理する場所 |
|---|---|---|
| プレイヤー作成 | +300 | `PlayerService/SignUp`(`player_service::STARTER_GEMS`) |
| 対戦の勝利 | +50 | `BattleInternalService/ReportResult`(`battle_service::BATTLE_WIN_REWARD_GEMS`) |
| 開発用の付与 | +450 | `DebugService/GrantGems` |
| スカウトの紹介 | −(バナーの `cost_per_roll`) | `ScoutService/Roll` |

- 増やすときは `INSERT ... ON DUPLICATE KEY UPDATE quantity = quantity + ?` で行が無くても作る
- 減らすときは `UPDATE ... SET quantity = quantity - ? WHERE quantity >= ?` の1文で、足りるかの判定と減算を同時に行う。
  更新した行が無ければ不足(行が無い場合を含む)として何も変えない
- 勝利報酬の額はマスタにせず定数で持つ。運用中に調整する想定が無いため
