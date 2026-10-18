---
title: "テストとボット"
status: current
area: battle-server
related: [battle-server/battle-flow, server/debug-api, client/testing]
code: [BattleServer/Tests, BattleServer/BattleBot, BattleServer/Battle/BattleBotPlayer.cs, BattleServer/BattleCore.Tests]
updated: 2026-10-18
---

# テストとボット

## テスト

- `Atlas.BattleCore` のロジックは、NUnit のテスト(`BattleServer/BattleCore.Tests/`)で乱数を固定して確かめる([バトルの計算](battle-core.md))
- バトルサーバーは xUnit。どちらも `dotnet test BattleServer.slnx` で一緒に回り、MySQL・APIサーバーを起動しなくても動く

| テスト | 内容 |
|---|---|
| `BattleHubTests` | インプロセスで起動したサーバーに2つの MagicOnion クライアントで接続する結合テスト。APIサーバーは `DummyParticipantDataSource`(固定のステータス・技)に差し替え、制限時間を短くする。ボットとの対戦(`BotMatch_*`)は、`bot_player_id` 入りのトークンで1つだけ接続して確かめる |
| `ApiParticipantDataSourceTests` | 内部 API の呼び出しと変換。APIサーバーは偽の gRPC サーバー(`FakeInternalApi`)にする |
| `BattleResultReporterTests` | 結果報告の再送と、`ALREADY_EXISTS` の扱い |
| `LoadoutBuilderTests` | 所持データ + マスタ → BattleCore の型、能力の式 |
| `MasterDatabaseFactoryTests` | マスタの読み込み |

## 自動対戦のクライアント(BattleBot)

サーバー全体(gRPC・マッチング・BattleServer・結果記録)を、本物のクライアントと同じ経路で通しで確かめるための、自動で対戦するクライアント(`make bot`)。

- マッチングで相手がいないときは、10秒待てばサーバーの中のボット(`BattleBotPlayer`)が相手になる([対戦の進行](battle-flow.md))。
  Unity で1人で対戦を確かめるだけなら、`make bot` は要らない
- `BattleBotPlayer` はこのクライアントを使わない。本番で使えない開発用 API に頼っており、別のプロセスとして起動しておく必要もあるため

- gRPC(Unity と同じ順番でデバイス登録 〜 サインイン)と MagicOnion で自動対戦する
- Unity 以外の .NET のクライアントなので基盤の `ApiCore` は使えず、`.proto` から `Grpc.Tools` で生成した素のクライアントに、
  `authorization`・`x-request-id` のヘッダーを自分で付ける(再認証・再送はしない)
- 基盤のデバイス登録の `.proto` は基盤のリポジトリにしか無いので、ビルドのときに基盤のタグの版を取ってくる
- 起動のたびに新しいプレイヤーを作り、`DebugService/RandomizeParty` でパーティをランダムにする([開発用API](../server/debug-api.md))
- 選出はパーティからランダム、技は使えるものからランダム、強制交代は生存している先頭の控え
- 演出が無いので、対戦の開始・ターン結果を受けたらすぐに準備(`ReadyForTurnAsync`)を送る。送らないと、相手が準備を送るたびに上限(15秒)まで待たせてしまう
- `--loop` で対戦のたびにマッチングに並び直し(Unity の相手として常駐させる)、`--count 2` でボット同士を対戦させる(サーバー全体を通しで確かめる)
