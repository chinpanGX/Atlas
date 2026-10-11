---
title: "APIサーバー"
status: index
area: server
updated: 2026-10-11
---

# APIサーバー

Rust(tonic + gRPC 基盤のクレート `voltech-grpc-*`)。ソースは `Server/src/`、開発の手順は [Server/CLAUDE.md](../../../Server/CLAUDE.md)。

## ページ

- [構成](structure.md) — ポート、Layer の順番、ディレクトリ、トランザクションとエラー、API 一覧
- [認証](auth.md) — デバイス認証(基盤)、`PlayerResolver`、`battle_token`、内部 API のシークレット
- [プレイヤー(サインアップ・サインイン)](player.md) — プレイヤーの作成と初期データ、所持データの全件
- [パーティ編成・技の付け替え](party.md) — `SetParty`・`SetPachimonMove`
- [スカウト](scout.md) — `ListBanners`・`Roll`・`Select`、バナーを DB に置く理由
- [マッチング](matchmaking.md) — メモリの待機列、成立と `battle_token`、ポーリング
- [内部API(バトルサーバー向け)](internal-api.md) — `BattleInternalService` の3つの RPC
- [結果報告が届かない対戦の後始末](stale-match-cleanup.md) — 定期的な打ち切り
- [開発用API](debug-api.md) — `DebugService` と有効にする条件
