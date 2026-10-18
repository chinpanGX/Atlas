---
title: "バトルサーバー"
status: index
area: battle-server
updated: 2026-10-18
---

# バトルサーバー

ASP.NET Core + MagicOnion(C#)。対戦の進行だけを受け持ち、状態はメモリに持つ。ClientとBattleServerで共有する
`Shared/BattleCore`(ダメージ計算・ターン処理)と `Shared/BattleContracts`(通信契約)もここで扱う。
開発のルールは [BattleServer/CLAUDE.md](../../../BattleServer/CLAUDE.md)。

## ページ

- [構成](structure.md) — プロジェクト、クラスの役割、実装の決まり(Gate・エラーの使い分け)、共有ソースの持ち方
- [バトルの計算(Atlas.BattleCore)](battle-core.md) — 依存と入口、Section とイベントの構造、ダメージ・能力・技の扱い
- [通信契約(Atlas.BattleContracts)](contracts.md) — Hub と Receiver、情報の見せ方
- [対戦の進行](battle-flow.md) — 制限時間、参加・選出・ターン・強制交代、決着、切断と再接続、ボットとの対戦
- [APIサーバーとの連携](api-integration.md) — 内部 API の呼び出し、結果報告の再送、マスターデータ
- [テストとボット](testing-and-bot.md) — BattleCore の NUnit、バトルサーバーの xUnit、自動対戦のクライアント(BattleBot)
