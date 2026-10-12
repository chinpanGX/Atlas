# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

C#/MagicOnionのバトルサーバーと、ClientとBattleServerで共有する`Shared/BattleCore`(ダメージ計算・ターン処理)・
`Shared/BattleContracts`(通信契約)。プロジェクト全体のルールは[ルートのCLAUDE.md](../CLAUDE.md)。

| 知りたいこと | 読む場所 |
|---|---|
| プロジェクト・クラスの役割・共有ソースの持ち方 | [バトルサーバーの構成](../docs/wiki/battle-server/structure.md) |
| 参加・選出・ターン・強制交代・時間切れ・再接続の進行 | [対戦の進行](../docs/wiki/battle-server/battle-flow.md) |
| BattleCoreの構造と計算式 | [バトルの計算](../docs/wiki/battle-server/battle-core.md) |
| Hub・Receiverと情報の見せ方 | [通信契約](../docs/wiki/battle-server/contracts.md) |
| 内部APIの呼び出し・結果報告の再送 | [APIサーバーとの連携](../docs/wiki/battle-server/api-integration.md) |
| 起動・テストのコマンド | [開発環境ガイド](../docs/wiki/development.md) |

## コーディングルール

### 共有ソース(`Shared/BattleCore`・`Shared/BattleContracts`)

- **Unityでもコンパイルされる**ため、Unityで使えない書き方をしない。csprojで`LangVersion`をUnityと同じ`9.0`にし、
  `ImplicitUsings`を無効にしているので、BattleServerのビルドで気づける(`using`は明示する)
- **`Shared/BattleCore/`・`Shared/BattleContracts/`の中にcsprojを置いたり、そこで`dotnet build`したりしない**。
  `bin/`・`obj/`がパッケージ内にでき、UnityがそのDLLを取り込んで同名アセンブリの重複(CS1704)になる。
  ビルドは`BattleServer/`側のcsproj経由で行う
- `record`を使う場合、Unity向けに`IsExternalInitPolyfill.cs`を同梱している(消さない)
- BattleCoreは`UnityEngine`・MagicOnion・通信の型・マスターデータの生成型に依存させない。入口は`BattleEngine.ProcessTurn`だけにし、
  乱数は`IRandomSource`、タイプ相性は`ITypeChart`で外から渡す
- 技ごとの個別の効果は、Sectionを変えずにイベント(`MoveHitEvent`)のハンドラとして足す。天候・特性・持ち物のイベントは作らない
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
  別のスレッドから来るため)。Gateを取ったまま内部APIを待つ箇所がある(参加時のパーティ取得、選出時の所持データ取得)
- 進行(タイマーを含む)は`BattleCoordinator`に置き、`BattleHub`には「どの対戦のどちら側か」だけを持たせる
- 制限時間は`BattleTimingOptions`から読み、コードに直接書かない(テストで短くするため)
- クライアントに返すエラーは`ReturnStatusException`で、`StatusCode`を使い分ける(検証の違反は`InvalidArgument`、
  今の状態では受け付けない操作は`FailedPrecondition`、内部APIに届かない等は`Unavailable`)
- クライアントへはHPを%(0〜100、生きていれば最低1)でしか送らない。HPの実数・ダメージ量は結果報告(内部記録)にだけ含める
- 相手の選出の種族は、場に出るまで送らない
- APIサーバーの内部APIは`Shared/proto/atlas/internal/v1/battle.proto`から`Grpc.Tools`で生成したクライアントで呼び、
  メタデータ`x-internal-secret`を付ける。結果報告は一時的な失敗だけ再送し、`ALREADY_EXISTS`は成功扱いにする

### テスト

- `Tests/BattleHubTests.cs`: インプロセスで起動したサーバーに2つのMagicOnionクライアントで接続する結合テスト。
  APIサーバーは`DummyParticipantDataSource`(固定のステータス・技、パーティは`PlayerId`の末尾の文字 + 枠番号)に
  差し替え、制限時間は`BattleTimingOptions`で短くする
- 内部APIを呼ぶ部分のテストは、偽のgRPCサーバー(`FakeInternalApi`)に向ける
- MySQL・APIサーバーを起動しなくても動く

### ビルド

- .NET 10 SDKを使う。Git BashのPATHではUnity付属の.NET 8 SDKが先に見つかり、`.slnx`が読めない・`NETSDK1045`になるため、
  `"C:/Program Files/dotnet/dotnet.exe"`を明示する(`make dev`・`make bot`はそうしている)
- BattleServerを起動したままビルドすると、DLLがロックされてコピーに失敗する(MSB3021)。止めてからビルドする
