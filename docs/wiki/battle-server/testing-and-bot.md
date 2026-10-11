---
title: "テストとボット"
status: current
area: battle-server
related: [battle-server/battle-flow, server/debug-api, client/testing]
code: [BattleServer/Tests, BattleServer/BattleBot, Shared/BattleCore/Tests]
updated: 2026-10-11
---

# テストとボット

## テスト

- `Atlas.BattleCore` のロジックは、Unity の EditMode テスト(`Shared/BattleCore/Tests/`)で乱数を固定して確かめる([バトルの計算](battle-core.md))
- バトルサーバーは xUnit(`dotnet test BattleServer.slnx`)。MySQL・APIサーバーを起動しなくても動く

| テスト | 内容 |
|---|---|
| `BattleHubTests` | インプロセスで起動したサーバーに2つの MagicOnion クライアントで接続する結合テスト。APIサーバーは `DummyParticipantDataSource`(固定のステータス・技)に差し替え、制限時間を短くする |
| `ApiParticipantDataSourceTests` | 内部 API の呼び出しと変換。APIサーバーは偽の gRPC サーバー(`FakeInternalApi`)にする |
| `BattleResultReporterTests` | 結果報告の再送と、`ALREADY_EXISTS` の扱い |
| `LoadoutBuilderTests` | 所持データ + マスタ → BattleCore の型、能力の式 |
| `MasterDatabaseFactoryTests` | マスタの読み込み |

## 対戦相手ボット(BattleBot)

1人で実サーバーの対戦を確かめるための、自動で対戦するクライアント(`make bot`)。

- gRPC(Unity と同じ順番でデバイス登録 〜 サインイン)と MagicOnion で自動対戦する
- Unity 以外の .NET のクライアントなので基盤の `ApiCore` は使えず、`.proto` から `Grpc.Tools` で生成した素のクライアントに、
  `authorization`・`x-request-id` のヘッダーを自分で付ける(再認証・再送はしない)
- 基盤のデバイス登録の `.proto` は基盤のリポジトリにしか無いので、ビルドのときに基盤のタグの版を取ってくる
- 起動のたびに新しいプレイヤーを作り、`DebugService/RandomizeParty` でパーティをランダムにする([開発用API](../server/debug-api.md))
- 選出はパーティからランダム、技は使えるものからランダム、強制交代は生存している先頭の控え
- `--loop` で対戦のたびにマッチングに並び直し(Unity の対戦相手を常駐させる)、`--count 2` でボット同士を対戦させる(サーバー全体を通しで確かめる)
