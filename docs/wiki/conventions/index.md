---
title: "共通の決まり"
status: index
area: conventions
updated: 2026-10-11
---

# 共通の決まり

Client・APIサーバー・バトルサーバーのどれにもかかわる決まり。

## ページ

- [命名とID](naming-and-ids.md) — `snake_case`/`camelCase`/`PascalCase` の使い分け、`pachimon`、ULID とマスタの ID
- [値なしの表し方(nullable)](nullable.md) — 番兵値、行が無いこと、経路ごとの nullable の可否
- [所持データの差分(player_diff)](player-diff.md) — 所持データを変える API の共通の形と、サインインの全件
- [所持リソース(アイテム)](items.md) — ジェムのような数量を `items`/`player_items` で持つ
- [業務エラー](errors.md) — `Shared/errors.toml` の reason と action、返し方
