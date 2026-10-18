---
title: "スカウト"
status: current
area: game
related: [server/scout, db/scout-tables, game/gems]
code: [Server/src/service/scout_service.rs, Shared/master-data/csv/scout_banners_master.csv, Client/AtlasUnityProject/Assets/Scripts/Presentation/Scout]
updated: 2026-10-11
---

# スカウト

本家ポケモンの「紹介(候補10体から1体を選ぶ)」をもとにした抽選。

- 開催中のスカウトから1つ選び、ジェムを払って「紹介」を受ける(現状は常設の「レギュラースカウト」のみ、1回150ジェム)
- 紹介ごとに候補10体が提示され、その中から1体を選んで入手する
  - 選ばなかった9体は消える
  - 候補を選ぶまでスカウト画面から戻れない(選ばずにアプリを終了すると、その紹介は選べなくなり、ジェムも戻らない)
- 候補10体は1体ずつ次の確率でレア度を決め、そのレア度の種族から等確率で選ぶ(同じ種族が複数出ることもある)

| レア度 | 確率 |
|---|---|
| S | 3% |
| A | 12% |
| B | 35% |
| C | 50% |

- 入手した個体は初期技4つを覚えている
- 排出率はクライアントに渡さない(表示もしない)

作りは [スカウト(API)](../server/scout.md)。
