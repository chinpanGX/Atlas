---
title: "テストとサーバー無しでの動作"
status: current
area: client
related: [client/services-and-player-data, battle-server/testing-and-bot, battle-server/battle-core]
code: [Client/AtlasUnityProject/Assets/Scripts/Infrastructure/Mock, Client/AtlasUnityProject/Assets/Scripts/Tests, Client/AtlasUnityProject/Assets/Scripts/DI/RootLifetimeScope.cs]
updated: 2026-10-17
---

# テストとサーバー無しでの動作

## APIサーバーの差し替え

- `RootLifetimeScope` の `CreateCallInvoker`(virtual)に通信の送り先を切り出している。本番は `ApiChannel`(gRPC のチャンネルを1つ使い回す。
  エディタと開発ビルドでは gRPC Monitor に通信が記録される)
- Battle PlayMode テスト用の派生スコープ(`TestRootLifetimeScope`)は、基盤の `FakeCallInvoker` に偽のサーバー(`FakeServer`)を登録したものに
  差し替える。ラッパー・`ApiCore`・反映処理は本物と同じに動く
- Service の単位で Mock を作らないのは、ラッパーと `ApiCore` を通らず、エラー処理・反映処理が本物と違う動きになるため

## 対戦の差し替え

- 対戦(`IBattleMatchMaker`/`IBattleConnectionFactory`)は `ConfigureBattleConnections`(virtual)に切り出し、Bootstrap シーンの
  `RootLifetimeScope` の Inspector(`Use Battle Server`)で切り替える。オンで実サーバー、オフで Mock
- `MockBattleConnection` は BattleCore をクライアントの中で動かし、相手は簡易 AI にする(技はランダム、自分から交代しない、強制交代は先頭の控え)。
  プレイヤーの強制交代ターンでは行動せず、自分の強制交代はプレイヤーを待たずに処理する
- Mock のパーティは `TestPartyFactory` がマスタから作る(努力値なし)
- `TestRootLifetimeScope` は、`Use Battle Server` に関わらず常に Mock にする

## テスト

| テスト | 種類 | 内容 |
|---|---|---|
| `Shared/BattleCore/Tests/` | EditMode | ダメージ計算・行動順・ターン処理。乱数とタイプ相性を固定して確かめる |
| `Tests/Infrastructure/Mock/` | EditMode | `MockBattleConnection`・`TestPartyFactory` |
| `Tests/Presentation/Party/` | EditMode | `PartyEditor`(編成の編集ロジック) |
| `Tests/Presentation/Battle/` | PlayMode | Bootstrap → Home → Battle の起動経路を、APIサーバー無しで通す |

- C# を変えたら `unity recompile --strict` でエラー・警告が0であることを確かめる。テスト(`unity command run_tests`)は頼まれたときだけ回す
- Client を実際に操作する結合テスト(パーティ編成 → マッチング → 対戦 → 結果)に [Anjin](https://github.com/DeNA/Anjin) を使う案がある(未着手)
