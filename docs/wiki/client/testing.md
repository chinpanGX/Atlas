---
title: "テスト"
status: current
area: client
related: [client/services-and-player-data, battle-server/testing-and-bot, battle-server/battle-core]
code: [Client/AtlasUnityProject/Assets/Scripts/Tests, Shared/BattleCore/Tests, Client/AtlasUnityProject/Assets/Scripts/DI/RootLifetimeScope.cs]
updated: 2026-10-18
---

# テスト

## テスト

| テスト | 種類 | 内容 |
|---|---|---|
| `Shared/BattleCore/Tests/` | EditMode | ダメージ計算・行動順・ターン処理。乱数とタイプ相性を固定して確かめる |
| `Tests/Presentation/Party/` | EditMode | `PartyEditor`(編成の編集ロジック) |
| `Tests/Presentation/Training/` | EditMode | `TrainingStatAllocation`(ステータスポイントの割り振りの調整と、必要なチケットの枚数) |

- C# を変えたら `unity recompile --strict` でエラー・警告が0であることを確かめる。テスト(`unity command run_tests`)は頼まれたときだけ回す
- 画面と通信を通す確認は、APIサーバーとバトルサーバーを起動して行う([開発環境ガイド](../development.md))。サーバーが無くても対戦できる
  仕組み(Mock)は無い
- Client を実際に操作する結合テスト(パーティ編成 → マッチング → 対戦 → 結果)に [Anjin](https://github.com/DeNA/Anjin) を使う案がある(未着手)

## APIサーバーの代わりにするとき

- 通信を使う処理をテストで動かすときは、Service を差し替えず、`CallInvoker` を基盤の `FakeCallInvoker`(偽のサーバー)に差し替える。
  ラッパー・`ApiCore`・反映処理は本物と同じに動く
- Service の単位で偽物を作らないのは、ラッパーと `ApiCore` を通らず、エラー処理・反映処理が本物と違う動きになるため

## 採用しなかった案

- **サーバー無しで対戦できる Mock を持ち続ける**: 以前は、対戦を `MockBattleConnection`(BattleCore を Client の中で動かし、相手は簡易 AI)に、
  APIサーバーを偽のサーバーに差し替えて、Unity だけで Bootstrap → Home → Battle を動かせた(PlayMode テストもこれで通していた)。
  実サーバーで対戦でき、相手がいなくてもサーバーのボットと対戦できるようになったため、実サーバーとの二重の実装を保つ手間に見合わず、消した
