---
title: "マスターデータ"
status: index
area: master-data
updated: 2026-10-11
---

# マスターデータ

正本は `Shared/master-data/` のスキーマ(`schema/tables/*.yaml`・`schema/enums/*.yaml`)と CSV。自作の
[master-data-pipeline](../../../master-data-pipeline)(submodule)が、Rust・C# 向けの型とデータを生成する。

## ページ

- [生成・配布・運用](pipeline.md) — 配布先ごとの生成物と読み込み方、パイプラインの制約、更新の手順
- [テーブル](tables.md) — 各マスタの列と件数、Enum
