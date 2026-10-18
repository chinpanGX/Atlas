---
title: "バトルサーバーの構成"
status: current
area: battle-server
related: [battle-server/battle-flow, battle-server/battle-core, battle-server/contracts, battle-server/api-integration]
code: [BattleServer/Program.cs, BattleServer/Hubs, BattleServer/Battle, BattleServer/Auth, BattleServer/Internal, BattleServer/BattleCore, BattleServer/BattleContracts]
updated: 2026-10-18
---

# バトルサーバーの構成

## プロジェクト

| プロジェクト | 場所 | 内容 |
|---|---|---|
| `BattleServer` | `BattleServer/` | ASP.NET Core + MagicOnion。Kestrel は開発中 HTTP/2 のみ・非 TLS |
| `Atlas.BattleCore` | ソースは `Shared/BattleCore/Runtime/`、csproj は `BattleServer/BattleCore/` | ダメージ計算・ターン処理。Client と同じソース |
| `Atlas.BattleContracts` | ソースは `Shared/BattleContracts/Runtime/`、csproj は `BattleServer/BattleContracts/` | 通信契約(Hub・Receiver・Payload)。Client と同じソース |
| `Atlas.MasterData` | `BattleServer/MasterData/` | マスターデータの生成物 |
| `BattleBot` | `BattleServer/BattleBot/` | 開発用の自動対戦クライアント。サーバー全体を通しで確かめるのに使う([テストとボット](testing-and-bot.md))。マッチングで相手がいないときのボット(`BattleBotPlayer`)とは別 |
| テスト | `BattleServer/Tests/` | xUnit |

- 公式の Getting Started・ChatApp サンプルと同じ最小構成(ASP.NET Core Empty + `MagicOnion.Server`)。専用のテンプレートは無い
- .NET 10
- APIサーバーが発行した `battle_token`(JWT)は `Microsoft.IdentityModel.JsonWebTokens` で検証する
- Docker 化はしない(APIサーバーと同じく、Windows でのビルドの速さとデバッグのしやすさを優先。デプロイの方式を決めるときに検討する)
- 起動時に、`BATTLE_TOKEN_SECRET`(32バイト以上)・マスターデータ・`INTERNAL_API_SECRET` のどれかが欠けていれば、接続を受け付ける前に起動を失敗させる

## クラスの役割

| 場所 | 役割 |
|---|---|
| `Hubs/BattleHub.cs` | StreamingHub。1接続1インスタンスで、「どの対戦のどちら側か」だけを持ち、処理は `BattleCoordinator` に委ねる |
| `Battle/BattleCoordinator.cs` | 全対戦の状態(`matchId` → `BattleSession`)と進行。タイマーは Hub の外で発火するため、進行はこのシングルトンに置く |
| `Battle/BattleSession.cs` | 1対戦の状態(フェーズ・参加者・パーティ・選出・盤面・タイマー・行動ログ)。`Gate`(セマフォ)を取ってから読み書きする。参加者ごとに通知の送り先(`Receiver`)を持つ |
| `Battle/BattleBotPlayer.cs` | マッチングで相手がいなかった人の対戦相手のボット。参加者の送り先として通知を受け、`BattleCoordinator` のメソッドで行動する([対戦の進行](battle-flow.md)) |
| `Battle/BattleTimingOptions.cs` | 制限時間類(テストで差し替える) |
| `Battle/IParticipantDataSource.cs` | パーティ・選出個体の取得とタイプ相性の抽象(実装は `Internal/ApiParticipantDataSource.cs`) |
| `Battle/LoadoutBuilder.cs` | 所持データ + マスタ → BattleCore の型(`ParticipantStats`/`MoveData`/`ITypeChart`)。レベル50の固定と能力の式もここ |
| `Battle/MasterDatabaseFactory.cs` | 起動時のマスタの読み込み |
| `Auth/BattleTokenValidator.cs` | `battle_token` の検証。期限切れ(署名は正当)は無効と区別する |
| `Internal/ApiParticipantDataSource.cs` | 内部 API(`GetParty`・`GetLoadouts`)の呼び出し |
| `Internal/BattleResultReporter.cs` | 結果報告(`ReportResult`。再送付き) |

- `BattleSession` のフィールドは、必ず `session.Gate` を取得してから読み書きする。Hub の呼び出しとタイマーの発火が別のスレッドから来るため
- クライアントに返すエラーは `ReturnStatusException` で、検証の違反は `InvalidArgument`、今の状態では受け付けない操作は `FailedPrecondition`、
  内部 API に届かない等は `Unavailable` を使い分ける

## 共有ソースの持ち方

- `Atlas.BattleCore` と `Atlas.BattleContracts` は Unity のローカルパッケージ(`Shared/`)として置き、BattleServer は同じソースをコンパイルするだけの
  csproj を `BattleServer/` 側に置く
- csproj を Unity のパッケージの外に置くのは、パッケージの中に置くとビルド成果物(`bin`/`obj`)の DLL を Unity が取り込み、同名アセンブリの重複
  (CS1704)になるため
- Unity で使えない書き方をサーバー側のビルドで見つけられるよう、`LangVersion` を Unity と同じ `9.0` にし、`ImplicitUsings` は無効にする
- `record` を使うため、Unity 向けに `IsExternalInitPolyfill.cs` を同梱している
- Unity 側は DLL にせずソースのまま使う(変更のたびに DLL を作る手間と、テスト・デバッグのしづらさを避けるため)
