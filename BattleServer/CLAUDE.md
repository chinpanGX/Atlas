# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

C#/MagicOnionのバトルサーバーと、ClientとBattleServerで共有する`Shared/BattleCore`(ダメージ計算・ターン処理)・
`Shared/BattleContracts`(通信契約)。プロジェクト全体のアーキテクチャと共通ルールは[ルートのCLAUDE.md](../CLAUDE.md)。
起動・テストのコマンドは[DEVELOPMENT.md](../DEVELOPMENT.md)。

## アーキテクチャ

### プロジェクト

| プロジェクト | ソースの場所 | 内容 |
|---|---|---|
| `BattleServer` | `BattleServer/` | ASP.NET Core + MagicOnion。開発中はHTTP/2のみ・非TLS |
| `Atlas.BattleCore` | `Shared/BattleCore/Runtime/`(csprojは`BattleServer/BattleCore/`) | ダメージ計算・ターン処理 |
| `Atlas.BattleContracts` | `Shared/BattleContracts/Runtime/`(csprojは`BattleServer/BattleContracts/`) | `IBattleHub`/`IBattleHubReceiver`とPayload |
| `Atlas.MasterData` | `BattleServer/MasterData/` | マスターデータの生成物(手で編集しない) |
| `BattleBot` | `BattleServer/BattleBot/` | 開発用の対戦相手ボット(RESTでサインイン〜マッチング、MagicOnionで自動対戦) |
| テスト | `BattleServer/Tests/` | xUnit(`dotnet test BattleServer.slnx`) |

`Shared/`の2つはUnityのローカルパッケージでもあり、Clientは同じソースをそのまま使う。BattleServerは同じソースを
コンパイルするだけのcsprojを`BattleServer/`側に置いて参照する。

### バトルサーバーの構成

| 場所 | 役割 |
|---|---|
| `Hubs/BattleHub.cs` | StreamingHub。1接続1インスタンスで、「どの対戦のどちら側か」だけを持ち、処理は`BattleCoordinator`に委ねる |
| `Battle/BattleCoordinator.cs` | 全対戦の状態(`matchId`→`BattleSession`)と進行(参加・選出・ターン・投了・切断と再接続・時間切れ・決着)。タイマーはHubの外で発火するため、進行はこのシングルトンに置く |
| `Battle/BattleSession.cs` | 1対戦の状態。フェーズは`WaitingForJoin`→`Selecting`→`InProgress`→`Finished` |
| `Battle/BattleTimingOptions.cs` | 制限時間類(テストで短くする) |
| `Battle/IParticipantDataSource.cs` | パーティ・選出個体の取得とタイプ相性の抽象。本実装は`Internal/ApiParticipantDataSource.cs`(APIサーバーの内部APIを呼ぶ) |
| `Battle/LoadoutBuilder.cs` | 所持データ + マスタ → BattleCoreの型(`ParticipantStats`/`MoveData`/`ITypeChart`)。レベル50固定と能力の計算式はここにある |
| `Auth/BattleTokenValidator.cs` | `battle_token`(HS256)の検証。期限切れ(署名は正当)は再接続に使えるよう、無効とは区別する |
| `Internal/BattleResultReporter.cs` | 結果報告(通信エラー・5xxは間隔を空けて再送、409は成功扱い) |

- 進行の流れ: 参加時にパーティを取得 → 2人そろったら`OnSelectionStart` → 選出時に選出個体の所持データを取得して
  BattleCoreの型にする → 両者そろったら`OnMatchStart` → 行動がそろうたびに`BattleEngine.ProcessTurn`して`OnTurnResult`
  → 決着で`OnBattleEnd`と結果報告
- 再接続は専用のメソッドを持たず、再接続した人にだけ現在の状態を送り直す(選出中は`OnSelectionStart`、対戦中は`OnMatchStart`)

### Atlas.BattleCore

- `UnityEngine`・MagicOnion・通信の型・マスターデータの生成型のどれにも依存しない。自前の最小の型だけを持つ
- 入口は`BattleEngine.ProcessTurn`だけ。行動の無い(時間切れ等)側は`null`を渡す
- 処理はSectionの階層に分け、技ごとの個別の効果はイベント(`MoveHitEvent`)のハンドラとして足す(Sectionを変えずに
  追加できるようにする)。天候・特性・持ち物は対象外なので、それらのイベントは作らない
- 乱数は`IRandomSource`、タイプ相性は`ITypeChart`で外から渡す(テストで固定する)
- マスタ + 所持データからBattleCoreの型への変換と、能力の計算は呼び出し側が持つ(BattleServerは`LoadoutBuilder`、
  ClientのMockは`TestPartyFactory`)

## コーディングルール

### 共有ソース(`Shared/BattleCore`・`Shared/BattleContracts`)

- **Unityでもコンパイルされる**ため、Unityで使えない書き方をしない。csprojで`LangVersion`をUnityと同じ`9.0`にし、
  `ImplicitUsings`を無効にしているので、BattleServerのビルドで気づける(`using`は明示する)
- **`Shared/BattleCore/`・`Shared/BattleContracts/`の中にcsprojを置いたり、そこで`dotnet build`したりしない**。
  `bin/`・`obj/`がパッケージ内にでき、UnityがそのDLLを取り込んで同名アセンブリの重複(CS1704)になる。
  ビルドは`BattleServer/`側のcsproj経由で行う
- `record`を使う場合、Unity向けに`IsExternalInitPolyfill.cs`を同梱している(消さない)
- 通信契約のPayloadはMessagePackの`[MessagePackObject]`と`[property: Key(n)]`で番号を付ける。Clientと同時に
  更新されないことを考え、既存のキーの番号は変えない
- 通信契約を変えたら、Client側の`IBattleConnection`(Domain)・`RealtimeBattleConnection`・`MockBattleConnection`、
  BattleBot、テストの`TestReceiver`も合わせて直す(`IBattleHubReceiver`のメソッドを足すと、実装していない側は
  コンパイルエラーになる)

### BattleServer

- private フィールドは`camelCase`(アンダースコアを付けない)。コンストラクタの引数と同じ名前になる場合は`this.`で区別する
- 命名ルールは直下の`.editorconfig`にあり、`Directory.Build.props`の`EnforceCodeStyleInBuild`で`dotnet build`でも
  警告(IDE1006)になる。ビルドの警告を0に保つ
- `Nullable`は有効。`null`を許す型には`?`を付ける
- `BattleSession`のフィールドは、必ず`session.Gate`を取得してから読み書きする(Hubの呼び出しとタイマーの発火が
  別のスレッドから来るため)。Gateを取ったままHTTP(内部API)を待つ箇所がある(参加時のパーティ取得、選出時の
  所持データ取得)
- クライアントに返すエラーは`ReturnStatusException`で、`StatusCode`を使い分ける(検証の違反は`InvalidArgument`、
  今の状態では受け付けない操作は`FailedPrecondition`、内部APIに届かない等は`Unavailable`)
- クライアントへはHPを%(0〜100、生きていれば最低1)でしか送らない。HPの実数・ダメージ量は結果報告(内部記録)にだけ含める
- 相手の選出の種族は、場に出るまで送らない

### テスト

- `Tests/BattleHubTests.cs`: インプロセスで起動したサーバーに2つのMagicOnionクライアントで接続する結合テスト。
  APIサーバーは`DummyParticipantDataSource`(固定のステータス・技、パーティは`PlayerId`の末尾の文字 + 枠番号)に
  差し替え、制限時間は`BattleTimingOptions`で短くする
- MySQL・APIサーバーを起動しなくても動く

### ビルド

- .NET 10 SDKを使う。Git BashのPATHではUnity付属の.NET 8 SDKが先に見つかり、`.slnx`が読めない・`NETSDK1045`になるため、
  `"C:/Program Files/dotnet/dotnet.exe"`を明示する(`make dev`・`make bot`はそうしている)
- BattleServerを起動したままビルドすると、DLLがロックされてコピーに失敗する(MSB3021)。止めてからビルドする
