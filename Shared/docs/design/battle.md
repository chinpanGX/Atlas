# バトル設計

マッチング〜リアルタイム対戦〜結果記録までの詳細設計。全体構成・命名規則は
[architecture.md](architecture.md)を参照。

## 実装アーキテクチャ(共通モジュール・段階的実装・自動テスト)

Unity Client、C#/MagicOnionサーバー(バトルサーバー、未作成)のどちらも同じダメージ計算・
行動順決定ロジックを持つ必要があるため、両者から参照できる共通モジュールとして切り離す。
その上で、クライアント単体→APIサーバー込み→バトルサーバー本番実装、という順に段階を
踏んで実装する。

バトルサーバーは`Client/`, `Server/`, `Shared/`と並ぶリポジトリルート直下に`BattleServer/`
として配置する(`Server/`がRust API用の名前のため、命名の対称性を取る)。以降の相対パス
表記(`../Shared/BattleCore/...`等)はこの配置を前提とする。

### 全体レイヤー構造

```
View層(Unity MonoBehaviour等)
  │ 直接呼び出し             ▲ R3で購読(ReactiveProperty/Observable)
  ▼                        │
Presenter層: BattlePresenter(Client)
  - IBattleConnectionを唯一保持
  - コールバックをBattleViewStateへ変換して反映
  │ IBattleConnection呼び出し(UniTask)     ▲ event
  ▼                                      │
Connection層: IBattleConnection(Client)
  ├─ MockBattleConnection(Stage 1/2)       ├─ RealtimeBattleConnection(Stage 3)
  │   Domain.MasterData→BattleCore用の型に変換  │   IBattleHubクライアントをラップするだけ
  │   Atlas.BattleCoreを直接呼ぶ                │   Atlas.BattleCoreは呼ばない(結果を受信のみ)
  │                                            │ ネットワーク(gRPC/StreamingHub, MagicOnion)
  ▼                                            ▼
Atlas.BattleCore(Shared、依存ゼロ)     ◄─呼ぶ─  バトルサーバー: IBattleHub実装(Stage 3のみ)
  Section/Event/EventHandler                    Domain.MasterData→BattleCore用の型に変換
                                                 ConcurrentDictionary<matchId, BattleState>
                                                 │ /internal/battle/result(対戦終了時)
                                                 ▼
                                         APIサーバー: Rust/Axum(Server/)
                                         /battle/queue*(マッチング)
                                         battle_matches/battle_turnsへ記録

横断的に存在: Domain.MasterData(master-data-pipelineが生成するC#型)。専用の共有パッケージは
作らず、pipeline自身の`run.sh client`/`run.sh realtime`がClient/バトルサーバー双方へ
同一の生成物を個別コピーする(architecture.md「マスターデータ運用」参照)。
Atlas.BattleCoreからは直接参照されない(呼び出し側が変換する)
```

| 層 | 主な責務 | 依存するもの | 動作するStage |
|---|---|---|---|
| View | UI描画、ボタン操作の受付 | `IBattlePresenter`, `BattleViewState`(R3) | 1〜3 |
| Presenter(`BattlePresenter`) | View↔Connectionの仲介、状態のローカル保持 | UniTask, R3, VContainer(DI) | 1〜3 |
| Connection(`IBattleConnection`) | Mock/Realの切り替え境界 | UniTask、(Mockのみ)`Domain.MasterData`、(Realのみ)MagicOnion Client | 1〜3(実装が変わる) |
| `Atlas.BattleCore` | ダメージ計算・行動順決定・Section/Event/EventHandler | なし(.NET BCLのみ) | 1〜3(呼び出し元が変わる) |
| バトルサーバー(`IBattleHub`) | 対戦の権威側の判定、ブロードキャスト | MagicOnion, `Atlas.BattleCore`, `Domain.MasterData` | 3のみ |
| APIサーバー(Rust) | マッチング、結果記録、報酬付与 | Axum, MySQL | 1〜3(マッチングはStage2から) |
| `Domain.MasterData`(master-data-pipeline生成) | マスタデータの型・実データ(Client/バトルサーバーへ個別コピー) | MessagePack, MasterMemory | 1〜3 |

### 共通モジュール(Atlas.BattleCore)

`Shared/BattleCore/`にUnity Package(UPM)として配置する。UPMはUnity専用の仕組みで
バトルサーバー(Unity外の通常の.NETプロジェクト)からは参照できないため、同じソース
フォルダを参照する**薄い`.csproj`をバトルサーバー側に置き**、両者が同一の`.cs`ファイル群を
コンパイルする形にする。

```
Shared/BattleCore/
  package.json                        -- UPMパッケージ定義
  Runtime/
    Atlas.BattleCore.asmdef            -- Unity用アセンブリ定義(UnityEngine参照禁止)
    BattleState.cs
    DamageCalculator.cs
    TurnResolver.cs                    -- 行動順決定・命中判定
    IRandomSource.cs                   -- 乱数注入用インターフェース
  Tests/
    Atlas.BattleCore.Tests.asmdef      -- Unity EditModeテスト
    DamageCalculatorTests.cs

BattleServer/BattleCore/
  Atlas.BattleCore.csproj              -- ../../Shared/BattleCore/Runtime/**/*.cs をCompile Includeするのみ
```

- Client側: `Packages/manifest.json`に`"com.atlas.battlecore": "file:../../Shared/BattleCore"`
  でローカルパッケージ参照
- バトルサーバー側: `BattleServer/BattleCore/Atlas.BattleCore.csproj`を`ProjectReference`し、
  `Atlas.BattleCore.dll`として参照する(Unityのasmdefと同じく別アセンブリにし、`internal`の境界を
  そろえる)。csprojは**Unityパッケージの外**に置く。パッケージ内(`Shared/BattleCore/`)に置くと
  ビルド成果物(`bin|obj`)もパッケージ内に出力され、UnityがそのDLLを取り込んでCS1704(同名アセンブリの
  重複)になるため。また、Unityでコンパイルできない書き方をサーバー側のビルドで検出できるよう、
  `LangVersion`をUnityと同じ`9.0`に固定し、`ImplicitUsings`(Unityには無い)は無効にしている
- Unity側はDLL化せずソースのローカルパッケージのまま使う(変更のたびにDLLをビルド・コピーする
  手間が増え、EditModeテストやデバッグもしづらくなるため)
- `Atlas.BattleCore`は`UnityEngine`・`MagicOnion`・ネットワーク関連の型に一切依存しない
  Pure C#で実装する。`IBattleHub`のDTO(`MoveRequest`等)やUnityの`Random`はこの層に
  持ち込まず、呼び出し側(Hub実装・Client側Connection層)で変換する
- 乱数(命中判定・急所・ダメージ`0.85〜1.00`)は`IRandomSource`経由で注入し、テスト時は
  シード固定・決定論的な実装に差し替えられるようにする
- `Atlas.BattleCore`は`Domain.MasterData`(master-data-pipelineが生成するC#型、
  [architecture.md](architecture.md)参照)の生成型にも依存しない。以下のような自前の
  最小型のみを持つ

  ```csharp
  public enum ElementType { Normal, Fire, Water, /* ... */ }
  public enum MoveCategory { Physical, Special, Status }

  public static class BattleConstants
  {
      // このゲームはバトルが主目的で経験値によるレベルアップを持たないため、
      // 全パチモンは固定レベル50(競技対戦フォーマットの慣例)として扱う。
      // player_pachimonにlevelカラムは無い(design/outgame.md参照)
      public const int FixedLevel = 50;
  }

  public readonly record struct ParticipantStats(
      int Level, int Hp, int Atk, int Def, int SpAtk, int SpDef, int Speed,
      ElementType PrimaryType, ElementType? SecondaryType);

  public readonly record struct MoveData(
      ElementType MoveType, MoveCategory Category, int BasePower, int Accuracy);
  ```

  マスタ(`Domain.MasterData`)+プレイヤー所持データ(努力値等)+`BattleConstants.FixedLevel`から
  `ParticipantStats`/`MoveData`への変換は、呼び出し側(Client Mockのマッピング処理・
  バトルサーバーのHub実装)がそれぞれ持つ。BattleCore自身はテストも含めて`Domain.MasterData`を
  一切必要としない

### 内部構造(Section / Event / EventHandler)

CEDEC 2026のポケモン・バトルシステムチーム講演(「ポケモン勝負の進化を支える、バトルシステム
の基盤設計と運用事例」)で紹介された設計思想を採用する。要点は2つ:

1. **Section**: ゲームロジックを再利用可能な最小単位に分割し、階層構造で表現する。Sectionには
   個別仕様(技ごとの追加効果等)の実装を含めない
2. **Event / EventHandler**: 個別仕様はSectionから発火される`Event`に反応する`EventHandler`
   として実装し、Section本体を変更せずに追加・削除できるようにする

Atlasのミニマム版ルールでは天候・特性・道具が恒久的に対象外([architecture.md](architecture.md)
参照)なので、GameFreak講演にあるような多数のEventHandler(晴れ・特性もうか等)は不要。現時点で
必要な拡張ポイントは「技の追加効果」(現状は効果なし、将来やけど付与等を追加する可能性がある)
の1箇所のみ。

```
ターン処理 Section(両者の行動 or タイムアウトによる非行動を受け取る)
├─ 強制交代チェック Section          -- 前ターンで瀕死になった側に交代を要求。未対応ならSkip
├─ 行動順決定 Section                -- 交代>技、技同士は素早さ比較。行動が無い側はSkip
└─ 行動実行 Section(行動順に1体ずつ、行動が無ければ何もしない)
    ├─ 交代 Section
    └─ 技効果 Section
        ├─ 命中判定 Section          -- IRandomSource使用
        ├─ ダメージ付与 Section
        │   └─ ダメージ計算 Section
        │       ├─ 攻撃力決定 Section
        │       ├─ 防御力決定 Section
        │       └─ ダメージ算出 Section    -- STAB・ITypeChart・急所・IRandomSource
        ├─ 瀕死チェック Section       -- HP0判定、選出3体全滅なら敗北確定
        └─ 技効果後処理 Section       -- ★Eventフック(技命中後Event)。現状EventHandler0個
```

- 「技効果後処理Section」は無条件に「技命中後Event」を発火するが、現時点では反応する
  EventHandlerが存在しないため実質何もしない。将来追加効果を実装する際は、この箇所に
  EventHandlerを追加するだけで済み、他のSectionは変更しない
- 天候・特性・道具用のEvent(攻撃力補正Event等)は、Atlasのルールでは恒久的に不要なため作らない
- `Atlas.BattleCore`の公開APIは、この`ターン処理Section`を呼び出す単一のエントリポイント
  (例: `BattleEngine.ProcessTurn(BattleState state, PlayerAction? p1, PlayerAction? p2)`)
  のみとする。呼び出し側(Mock/Hub)はSection/Event/EventHandlerの内部構造を知らなくてよい

### IBattleConnection / Payload定義

Client側の画面・進行制御は、`IBattleHub`/`IBattleHubReceiver`と対になるメソッド構成を持つ
`IBattleConnection`のみに依存する(`Atlas.BattleCore`や`Domain.MasterData`を直接知らない)。
`MockBattleConnection`/`RealtimeBattleConnection`はこの同じインターフェースを実装する。

```csharp
public interface IBattleConnection
{
    UniTask<JoinResult> JoinAsync(string battleToken, string matchId);
    UniTask SubmitSelectionAsync(string[] playerPachimonIds);
    UniTask SubmitMoveAsync(MoveRequest move);
    UniTask SwitchAsync(int partySlot);
    UniTask ForfeitAsync();

    event Action<BattleStartPayload> OnMatchStart;
    event Action<TurnResultPayload> OnTurnResult;
    event Action<BattleEndPayload> OnBattleEnd;
    event Action OnOpponentDisconnected;   // 切断猶予中の表示用("対戦相手の接続待ち…")
    event Action OnOpponentReconnected;
}

// battleTokenが無効/期限切れ等の失敗をUIへ伝えるための戻り値。
// IBattleHub.JoinAsync(下記)と同じ型を共有し、RealtimeBattleConnectionは
// そのままパススルーするだけでよい
public enum JoinResultStatus { Success, InvalidToken, MatchNotFound, AlreadyJoined }
public record JoinResult(JoinResultStatus Status);

public record MoveRequest(string MoveId);

public record BattleStartPayload(
    ParticipantSnapshot Self, ParticipantSnapshot Opponent, int TurnTimeLimitSeconds,
    PachimonMoveSet[] SelfMoves);
// SelfMovesは自分側の選出各枠の技(Self.SelectedPachimonとインデックスが対応、Movesのインデックスが
// 技スロット)。Clientは手元の所持データではなくこれを表示・送信し、サーバーが判定に使う技と常に一致させる
// (BattleServerが扱う技・ステータスはBattleServer側のデータが正。相手側の技は非公開のため送らない)
public record PachimonMoveSet(MoveState[] Moves);
public record MoveState(string MoveId, int CurrentPp, int MaxPp);  // CurrentPpは送信時点の残りPP
// TurnTimeLimitSecondsはUIのターンタイマー表示用。新しいターンの開始(OnMatchStart/
// OnTurnResult受信)を合図に、Clientローカルでこの秒数からカウントダウンを表示する
// (サーバー側の実際のタイムアウト判定とは別のローカル表示用タイマー)

public record ParticipantSnapshot(
    string PlayerId, PachimonSlot[] SelectedPachimon, int ActivePachimonIndex);

public record PachimonSlot(bool IsRevealed, PachimonBattleState? State);
// 選出3体のうち「場に出た(=公開された)」枠だけをIsRevealed=trueにする。
// - Self: 3体とも常にIsRevealed=true(自分のことなので最初から全部見せる)
// - Opponent: 開始時点はActivePachimonIndexの1体だけIsRevealed=true、残り2体は
//   State=null(未公開の間は必ず生存・満タンHPなので値自体を送らない)。
//   一度公開された枠は、そのパチモンが控えに戻っても公開されたままにする
// これにより「選出3体いる/何体倒れたか」という枠の存在自体は見えるが、未公開の枠が
// 具体的にどの種族かは場に出るまで分からない、という状態を表現する

public record PachimonBattleState(
    string PlayerPachimonId, int PachimonId, int HpPercent, bool IsFainted);
// Levelは持たない。全パチモン共通の固定値(Atlas.BattleCore.BattleConstants.FixedLevel = 50)
// なので送信不要(UI側で「Lv.50」と固定表示すればよい)
// HpPercentは0〜100の整数(丸め)。CurrentHp/MaxHpの生値は自分側・相手側とも送らない。
// 生値を送るとダメージ量(=ダメージ計算式の出力そのもの)が丸わかりになり、STAB・急所
// 倍率・乱数範囲といった定数を逆算されやすくなるため

public record TurnResultPayload(
    int TurnNumber, ActionResult[] Actions, string[] PlayersRequiringForcedSwitch);

public record ActionResult(
    string PlayerId, ActionType Type, string? MoveId,
    bool Hit, bool Critical, EffectivenessResult Effectiveness,
    int TargetRemainingHpPercent, bool TargetFainted,
    int? NewActiveIndex, PachimonBattleState? RevealedPachimon);
// DamageDealt(実数のダメージ量)は同じ理由で送らない。TargetRemainingHpPercentのみを
// 送り、実際に何%減ったかはPresenterが直前に保持していた値との差分で表現する(表示上の
// アニメーション用であり、厳密なダメージ量として使わない)
// Switch時: NewActiveIndexに交代後のインデックスを入れる。相手側の枠が今回初めて
// 場に出た(初公開)場合のみRevealedPachimonに種族等を入れる。既知の枠への
// 再出し・自分自身の交代の場合はnullでよい(Presenter側で既に保持している情報を使う)

public enum ActionType { Move, Switch, Skip }   // Skip = タイムアウト or 強制交代未対応

// type_chartマスタのeffectiveness(ENUM)とは別物。Atlas.BattleCoreが結果を表現するための型
public enum EffectivenessResult { Immune, NotVeryEffective, Normal, SuperEffective }

public record BattleEndPayload(string WinnerId, BattleEndReason Reason);
// WinnerIdが空文字の場合は勝者なし(両者とも選出しなかった・両者とも放置した等。下記「選出の制限時間」
// 「放置」参照)。api-codegenの方針(nullableを使わない)に合わせ、nullではなく空文字で表す

public enum BattleEndReason { AllFainted, Forfeit, DisconnectTimeout }
```

- `TurnResultPayload.PlayersRequiringForcedSwitch`に含まれるプレイヤーは、次ターンは
  `SwitchAsync`以外のアクションを受け付けない(`SubmitMoveAsync`は無視 or 拒否する)

### UI層との連携(View / Presenter)

同じ画面内のView→Presenter通知にpub/subライブラリは使わず、R3のObservable直接購読のみで
実装する(Supplementの`IMessageBroker`(ZeroMessenger実装)は画面をまたぐ通知専用に限定導入。
詳細は[client-architecture.md](client-architecture.md)「画面をまたぐ通知(IMessageBroker)」参照)。
`IBattleConnection`を直接保持するのは`BattlePresenter`のみで、Viewは`IBattleConnection`/
`Atlas.BattleCore`の型を一切知らない。

> **注記**: 本節(バトル画面のView/Presenter)は`client-architecture.md`の初期設計より前に
> 書かれたもので、`BattleViewState`のReactivePropertyをViewが購読する向きになっている。
> 実際にバトル画面を実装する際は、`client-architecture.md`で確定した「Presenterが具象Viewを
> コンストラクタ注入で受け取りSubscribeする」向きに合わせて書き直すこと。

- **View→Presenter(ユーザーの意図)**: pub/subではなく、インターフェース経由の直接呼び出し
  ```csharp
  public interface IBattlePresenter
  {
      void OnMoveSelected(string moveId);
      void OnSwitchSelected(int partySlot);
      void OnForfeit();
  }
  ```
- **Presenter→View(起きたことの通知)**: R3の`ReactiveProperty<T>`/`Observable<T>`で表現し、
  Viewはこれを購読するだけ
  ```csharp
  public sealed class BattleViewState
  {
      public ReactiveProperty<PachimonSlot[]> SelfParty { get; } = new();
      public ReactiveProperty<int> SelfActiveIndex { get; } = new();
      public ReactiveProperty<PachimonSlot[]> OpponentParty { get; } = new();
      public ReactiveProperty<int> OpponentActiveIndex { get; } = new();
      public ReactiveProperty<int> TurnTimeRemainingSeconds { get; } = new();
      public ReactiveProperty<bool> SelfRequiresForcedSwitch { get; } = new();
      public ReactiveProperty<bool> OpponentDisconnected { get; } = new();
      public Observable<TurnResultPayload> TurnResultObserved => _turnResultSubject;
      public Observable<BattleEndPayload> BattleEndObserved => _battleEndSubject;

      private readonly Subject<TurnResultPayload> _turnResultSubject = new();
      private readonly Subject<BattleEndPayload> _battleEndSubject = new();
  }
  ```
- Payloadは全体スナップショットではなく差分(ターンごとのイベント)しか送らないため、
  `BattlePresenter`は`SelfParty`/`OpponentParty`を**ローカルで保持・更新する**責務を持つ
  - `OnMatchStart`受信時: `BattleStartPayload`の内容で`SelfParty`/`OpponentParty`を初期化
  - `OnTurnResult`受信時: 各`ActionResult`の`TargetRemainingHpPercent`/`TargetFainted`で
    HP・瀕死状態を更新し、`NewActiveIndex`があれば`SelfActiveIndex`/`OpponentActiveIndex`を
    更新し、`RevealedPachimon`があれば該当する相手の枠を`IsRevealed = true`に書き換える
    (一度公開した枠は二度と`IsRevealed = false`に戻さない)
  - HPバーは`PachimonBattleState.HpPercent`をそのまま表示に使う(サーバーが既に%で送るため
    View側での計算は不要)。ダメージ演出(何%減ったか)はPresenterが更新前の値との差分から
    求める
  - `TurnResultPayload.PlayersRequiringForcedSwitch`→`SelfRequiresForcedSwitch`、
    `OnOpponentDisconnected`/`OnOpponentReconnected`→`OpponentDisconnected`、
    `OnMatchStart`の`TurnTimeLimitSeconds`からローカルカウントダウンを開始して
    `TurnTimeRemainingSeconds`を更新、という変換もここで行う
- `IBattlePresenter`/`BattleViewState`はVContainerでSingleton登録し、View側はコンストラクタ
  注入で受け取る
- 以下はミニマム版のスコープ外として明確に対象外とする: 送信済み行動の取り消し、対戦中の
  簡易チャット/リアクション、観戦機能

### 技選択時のPP制限(UI側)

`Atlas.BattleCore`はPP0の技が渡されると例外を投げるのみで、選ばせないための防止策は
呼び出し側の責務(上記「PP消費」参照)。Atlasでは**Viewが残りPP0の技をボタン非活性/
非表示にする**ことでこれを担保する(送信そのものを防ぐ)。

- 各技の`max_pp`は`Domain.MasterData`(`moves`テーブル、静的データ)から取得できるが、
  対戦中に何回消費したか(残りPP)は`PachimonBattleState`/`ActionResult`のどちらにも
  専用フィールドを持たない。他のフィールド同様「差分のみ送り、フルスナップショットは
  送らない」設計方針(上記「Payloadは全体スナップショットではなく差分〜」参照)に
  合わせ、残りPP用の新規フィールドをPayloadに追加することはしない
- 代わりに`BattlePresenter`がローカルで導出する: 自分側のパチモンが場に出た時点で
  そのパチモンの技一覧の`max_pp`を初期値として`MoveId`ごとに保持し、以後`OnTurnResult`で
  `ActionResult.PlayerId == self`かつ`Type == Move`を受信するたびに該当`MoveId`の残りPPを
  1減算する(命中/外れ/状態技を問わず消費するBattleCore側の仕様と一致させる)
- `BattleViewState`に技ごとの残りPPを公開する状態
  (例: `ReactiveProperty<IReadOnlyDictionary<string, int>> SelfMovePp`)を追加し、
  Viewは残りPPが0の`MoveId`をボタン非活性/非表示にする
- 相手側の技PPはUIに表示しないため(仕様上不要)、この導出・保持は自分側のみで行う

### 段階的実装ロードマップ

| Stage | 内容 | 疎通範囲 |
|---|---|---|
| 0 | `Atlas.BattleCore`の骨組み作成(ディレクトリ・asmdef・csproj・EditModeテスト雛形) | なし |
| 1 | クライアント単体でバトルが完結することを確認 | Client内のみ(ネットワーク通信なし) |
| 2 | APIサーバー(Rust)込みで疎通確認 | Client ⇔ Rust(`/battle/queue*`) |
| 3 | バトルサーバー(MagicOnion)へ本番ロジックを移植 | Client ⇔ バトルサーバー ⇔ Rust(内部API) |

**Stage 1(クライアント単体)**

- `IBattleConnection`(定義は上記「IBattleConnection / Payload定義」参照)を実装した
  `MockBattleConnection`を使う
- `MockBattleConnection`はネットワークを介さず、`Atlas.BattleCore`をプロセス内で
  そのまま呼び出して両プレイヤー分の行動を解決する(対戦相手は簡易AIで代替)
- 簡易AIの挙動: 場に出ているパチモンの使用可能な技から`IRandomSource`でランダムに
  1つ選ぶ(強さ・タイプ相性は考慮しない)。自発的な交代は行わず、強制交代(瀕死)の
  場合のみ選出3体のうち生存している先頭のパチモンに交代する。タイムアウト/投了は
  意図的に発生させず、毎ターン必ず有効な行動を返す(Anjin等での結合テストを安定
  させることが目的のため)
- `battleToken`/`matchId`もこの段階ではダミー値で構わない

**Stage 2(APIサーバー込み)**

- Rust側に`/battle/queue`系(待機列参加・離脱・マッチ成立確認)を実装し、実際の
  マッチング〜`battleToken`発行までを疎通させる
- バトルサーバーはまだ無いため、`battleToken`取得後のバトル本体は引き続き
  `MockBattleConnection`(Stage 1のロジック)で処理する。つまり「マッチングは本物、
  対戦はMock」という組み合わせで、APIサーバー連携部分だけを先に固める
  - 「対戦はMock」の意味: 実際に2人のクライアントがマッチングされても、各クライアントは
    相手の実際の行動を受け取らず、各自ローカルでMock対戦(相手役はスクリプト/簡易AI)を
    進める。この段階の目的は「マッチングAPIの疎通確認」であり、2人同時のMock対戦合わせを
    実現するものではない

**Stage 3(バトルサーバー移行)**

- `バトルサーバー`プロジェクトを新規作成し、`BattleServer/BattleCore/Atlas.BattleCore.csproj`を
  `ProjectReference`で参照(上記「共通モジュール」参照)
- `IBattleHub`(既存定義、下記参照)を実装し、ダメージ計算・行動順決定は
  `Atlas.BattleCore`をそのまま呼び出す(Stage 1で書いたロジックを再実装しない)
- Client側は`MockBattleConnection`を`RealtimeBattleConnection`(実際のMagicOnion
  StreamingHubクライアント)に差し替えるだけで、呼び出し元(UI・進行制御)は変更不要
- 対戦終了時の`/internal/battle/result`呼び出しもこの段階で実装する
- 実装済みの構成:
  - 通信契約(`IBattleHub`/`IBattleHubReceiver`/Payload)は`Shared/BattleContracts/`(Unityのローカルパッケージ、
    名前空間`Atlas.BattleContracts`)に置き、BattleServerは`BattleServer/BattleContracts/Atlas.BattleContracts.csproj`
    (`Atlas.BattleCore`と同じくパッケージ外のcsproj)で同じソースをビルドする
  - Clientはマッチング(`IBattleMatchmaker`: `POST /battle/queue`→`GET /battle/queue/status`を1秒間隔で確認、
    Homeの`MatchmakingModal`でキャンセル可)→`RealtimeBattleConnection`(`Atlas.Infrastructure.Realtime`、
    YetAnotherHttpHandlerでHTTP/2(h2c)接続、MagicOnionのSource Generatorで生成したクライアント)の順で接続する。
    選出はパーティの枠番号順に先頭から最大3体。`IBattleConnection`は`IBattleConnectionFactory`でBattleシーンの
    スコープごとに生成し、シーン終了時に切断する
  - Mock/実サーバーはBootstrapシーンの`RootLifetimeScope`の`Use Battle Server`で切り替える
    (確認方法はリポジトリ直下のDEVELOPMENT.md「対戦(実サーバー)の確認方法」)

### 自動テスト方針

- `Atlas.BattleCore`単体のロジック(ダメージ計算式・タイプ相性・命中判定等)は
  Unity EditModeテスト(`Tests/`配下)でカバーする。`IRandomSource`をシード固定の
  実装に差し替えて決定論的に検証する
- クライアントを実際に操作しての結合テスト(パーティ編成→マッチング→バトル進行→
  結果画面)には[Anjin](https://github.com/DeNA/Anjin)を使う。Anjinはシナリオに
  沿った自動操作でアプリを実行し、例外発生やクラッシュを検出するツールであり、
  ダメージ値等の厳密な数値アサーションはEditModeテスト側の責務とする(Anjinは
  「バトルが最後まで例外なく完走するか」を確認する役割に絞る)
- Anjin導入自体はUnityプロジェクトの体裁(`ProjectSettings/`等)が整い、対戦画面の
  UIが最低限動く状態になってから着手する(現状Client側はマスターデータの生成物
  のみで、Unityプロジェクトとして未構築のため)

## API仕様(マッチング、Rust側)

| # | メソッド | パス | 説明 | 認証 |
|---|---|---|---|---|
| 1 | POST | `/battle/queue` | 待機列に参加 | 要 |
| 2 | DELETE | `/battle/queue` | 待機列から離脱 | 要 |
| 3 | GET | `/battle/queue/status` | マッチ成立確認(polling) | 要 |
| 4 | POST | `/internal/battle/result` | 対戦結果・ログをまとめて記録(内部API) | サービス間シークレット |
| 5 | POST | `/internal/battle/loadouts` | 選出個体の所持データ取得(内部API) | サービス間シークレット |

### 1. マッチング待機列に参加

```
POST /battle/queue
```

パーティが1体以上編成されていることが前提(パーティ編成は[outgame.md](outgame.md)参照)。
未編成なら`400`。成功時は`200`(ボディ無し)で、マッチ成立の有無は`GET /battle/queue/status`で
確認する。既に待機中・マッチ成立済み(結果未取得)の場合は何もしない(二重参加しても自分自身と
マッチしない)。

### 2. マッチング待機列から離脱

```
DELETE /battle/queue
```

待機列にいない場合も`200`(ボディ無し)。

### 3. マッチ成立確認

```
GET /battle/queue/status
```

polling方式(1秒程度の遅延はゲーム体験に影響しないため、WSを別途入れるコストに対して
メリットが薄いと判断)。マッチング待機列自体はDB永続化せず、Rustプロセスのメモリで
管理する(サーバー1台構成のため問題なし)。

**実装方式**: `Mutex<VecDeque<player_id>>`(待機列)+`HashMap<player_id, MatchInfo>`
(マッチ済み結果の一時保持)を`AppState`に持たせ、`POST /battle/queue`が呼ばれた時点で
即座に同期的にペアリングする(専用のチャネル・バックグラウンドタスクは使わない)。
1vs1の単純なマッチングにこの規模の非同期処理は過剰と判断した。
- `POST /battle/queue`: 待機列に自分(`player_id`)を追加 → 他に待機者が1人以上いれば
  先頭の1人を取り出しペア成立、両者分の`MatchInfo`(matchId/battleToken/battleServer)を
  `matched`に保存する
- `GET /battle/queue/status`: `matched`に自分がいれば下記レスポンスを返し**取得後は
  そのエントリを削除**する。まだ`waiting`にいるだけなら`{"status": "waiting"}`を返す

レスポンス(未マッチ時)

```json
{ "status": "waiting", "matchId": "", "battleServer": "", "battleToken": "" }
```

`api-codegen`がOpenAPIの`nullable`に対応しないため(`Option<T>`を使わない方針、progress.md参照)、
未マッチ時もフィールド自体は常に存在し、空文字を返す。クライアントは`status`で判定する。

レスポンス(マッチ成立時)

```json
{
  "status": "matched",
  "matchId": "01J...",
  "battleServer": "https://battle.example.com",
  "battleToken": "短命JWT(match_id, player_idを含む)"
}
```

**`battleToken`の実装方式**: 標準的なJWT(HS256)。Rust側は`jsonwebtoken`クレートで
発行し、BattleServer側は`Microsoft.IdentityModel.JsonWebTokens`で検証する(自前の
HMAC署名フォーマットはRust/C#双方で設計・実装コストが高いため不採用)。claimsは
`match_id`/`player_id`のみ、有効期限は30秒(マッチ成立直後に接続するだけなので短くて
よい)。共有シークレットは両サーバーとも環境変数(`BATTLE_TOKEN_SECRET`)で配布する。
有効期限は`exp`クレーム(UNIX秒)で表現する。検証側のclock skew許容(`jsonwebtoken`の
`Validation`はデフォルト60秒、`Microsoft.IdentityModel`の`ClockSkew`はデフォルト5分)が30秒より
大きいと実質の有効期限が延びるため、BattleServer側では`ClockSkew`を小さく設定する。
クライアントへ返す`battleServer`はRust側の環境変数`BATTLE_SERVER_URL`で設定する。

### 4. 対戦結果記録(内部API)

```
POST /internal/battle/result
```

MagicOnionサーバーから対戦終了時に呼び出される。内部ネットワークのみ疎通、
サービス間シークレットで保護する(エンドユーザーの`access_token`とは別軸の認証)。
シークレットは`X-Internal-Secret`ヘッダーで送り、共有値は両サーバーとも環境変数
`INTERNAL_API_SECRET`で配布する(Rust側は必須、未設定だと起動時panic)。Rust側は
定数時間比較で照合し、不一致・欠落なら`401`。Unity Clientからは呼ばないため、Rust側の
OpenAPI(`ApiDoc`)には載せない(`api-codegen`の生成対象にしない)。

- 通信エラー・`5xx`の場合、BattleServerは間隔を空けて再送する(既定1秒/5秒/15秒の計3回)。`409`(記録済み)は
  成功扱い、それ以外の`4xx`は再送しない。再送し切っても失敗した場合の永続化は未対応
- `player1`/`player2`はBattleServerに先に`JoinAsync`した側を`player1`とする。`battle_matches`の
  `player1_id`/`player2_id`(マッチ成立時の順番)とは一致するとは限らないため、`player1Id`/`player2Id`を
  同梱し、Rust側はIDで突き合わせて保存する
- `turns`は1ターン内の行動ごとに1要素(`battle_turns`の1行)。`actionData`は
  `{ "type": "Move"|"Switch"|"Skip", "moveId", "partySlot" }`、`resultData`は
  `{ "hit", "critical", "effectiveness", "damageDealt", "targetRemainingHp", "targetFainted", "newActiveIndex" }`
  (内部記録用のため、クライアントへは送らないHPの生値も含める)
- 対戦開始前に相手が一度も`JoinAsync`しなかった場合も、猶予時間経過で参加済み側の勝利
  (`DisconnectTimeout`)として報告する。未参加側の`player2Id`は空文字、`player2SelectedPachimon`は空配列になる

リクエスト

```json
{
  "matchId": "...",
  "winnerId": "...",
  "player1Id": "...",
  "player2Id": "...",
  "player1SelectedPachimon": ["...", "...", "..."],
  "player2SelectedPachimon": ["...", "...", "..."],
  "turns": [
    { "turnNumber": 1, "playerId": "...", "actionData": { }, "resultData": { } }
  ]
}
```

`battle_matches`のステータス更新・`winner_id`設定と、`battle_turns`への一括INSERTを行う。
このタイミングで勝者へのgems付与も行う(詳細は下記「報酬設計(gems)」参照)。
成功時は`200`(ボディ無し)。

- `battle_matches`の行はマッチ成立時(`POST /battle/queue`でペアが決まった時点)に
  `status = 'in_progress'`・`started_at`付きで作成する。`player1_id`は先に待機列で待っていた側
- 選出(`player1_selected_pachimon`/`player2_selected_pachimon`)は、報告の`player1Id`/`player2Id`と
  `battle_matches`側の参加者IDを突き合わせて正しい列へ保存する。未参加側は空配列`[]`を保存する
- `actionData`/`resultData`は中身を解釈せず、そのままJSON列に保存する
- 処理は1トランザクションで行い、対象の`battle_matches`行を`FOR UPDATE`でロックする
  (二重報告が同時に届いても2件目は`409`になり、gemsは二重付与されない)
- `winnerId`が空文字の場合は勝者なし(両者未選出・両者放置等、下記「選出の制限時間」「放置」参照)。
  `status = 'aborted'`にし、`winner_id`はNULLのまま、gemsは付与しない。選出・ターンは通常どおり保存する
- 報告済みかどうかは選出列(`player1_selected_pachimon`)がNULLでないかで判定する(結果報告は必ず設定し、
  下記「結果報告が届かない対戦の後始末」は設定しないため)。後始末で`aborted`になった対戦に後から届いた
  報告は受け付け、報告内容で上書きする(勝者ありなら`finished`にしてgemsも付与する)

| 状況 | ステータス |
|---|---|
| `X-Internal-Secret`が不一致・欠落 | `401` |
| `matchId`が存在しない | `404` |
| `winnerId`(空文字は除く)・`player1Id`/`player2Id`(空文字は除く)・ターンの`playerId`がその対戦の参加者でない | `400` |
| 既に結果報告済み(二重報告。勝者なしで報告済みの場合も含む) | `409`(何も変更しない) |

### 5. 選出個体の所持データ取得(内部API)

```
POST /internal/battle/loadouts
```

BattleServerが選出(`SubmitSelectionAsync`)を受け取ったときに呼ぶ。`/internal/battle/result`と同じく
`X-Internal-Secret`ヘッダーで保護し、OpenAPIには載せない。プレイヤーごとに異なる所持データ
(どのパチモンか・努力値・覚えている技)だけを返し、種族値・タイプ・技の性能・タイプ相性は
BattleServerが自分のマスタ(`MemoryDatabase`)から引く(`LoadoutBuilder`で`ParticipantStats`/`MoveData`/
`ITypeChart`へ変換)。

リクエスト

```json
{ "playerId": "...", "playerPachimonIds": ["...", "...", "..."] }
```

レスポンス(`pachimon`はリクエストの`playerPachimonIds`と同じ順番、`moveIds`はslot順)

```json
{
  "pachimon": [
    {
      "playerPachimonId": "...",
      "pachimonId": 1031,
      "effortValues": { "hp": 0, "atk": 0, "def": 0, "spatk": 0, "spdef": 0, "speed": 0 },
      "moveIds": [33, 19, 58, 45]
    }
  ]
}
```

- `playerId`の所持でない個体(他プレイヤーの個体・存在しないID)が1体でも含まれれば`404`。
  BattleServerは選出を`InvalidArgument`で拒否する
- APIサーバーに繋がらない等で取得できなかった場合、BattleServerは`Unavailable`を返し、選出は未確定のまま
  残す(クライアントは同じ選出を送り直せる)
- 所持データの取得を選出時にまとめて1回だけ行うのは、対戦中にパーティ編成・技の付け替えをしても
  進行中の対戦に影響させないため

検討したが採用しなかった案:

- BattleServerがMySQLを直接読む: DBアクセスをRust経由に統一する方針から外れるため不採用
- マッチ成立時に`battleToken`(JWT)へパーティ全員分の所持データを埋め込む: 選出はBattleServerが受け取る
  ため、使わない個体の分までトークンに載ることになり、トークンの役割(参加資格の証明)とも混ざるため不採用

### 結果報告が届かない対戦の後始末

BattleServerからの結果報告が届かないまま`in_progress`で残る対戦がある。

- BattleServerの再起動・クラッシュ(対戦状態はメモリにしか無い)
- マッチ成立後、両者ともBattleServerに一度も接続しなかった(BattleServerは対戦の存在を知らない)
- 結果報告の再送失敗

APIサーバーは5分ごとに、`started_at`から**1時間**以上経った`in_progress`の対戦を`aborted`にする
(`battle_service::spawn_stale_match_cleanup`、`main.rs`で起動)。対戦時間には上限が無いため1時間を
超える正当な対戦も打ち切られ得るが、その後に届いた結果報告は受け付けて上書きするため、一時的に
`aborted`と表示されるだけで実害は無い。

- 検討したが採用しなかった案: BattleServer起動時にAPIサーバーへ「進行中の対戦を全て打ち切る」よう通知する。
  再起動のケースは1時間待たずに片付くが、定期処理だけで全ケースを賄えるため見送った

## 報酬設計(gems)

対戦結果に応じてgemsを付与する報酬設計。デイリーミッション・ログインボーナスなど、
対戦以外の経路での報酬は今回のスコープに含めない。

### 初期付与

プレイヤー作成時(`POST /signup`、[outgame.md](outgame.md)参照)に、初期gemsとして
`300`を付与する(`player_items`に`item_id: 1`・`quantity: 300`の行を作成、
[architecture.md](architecture.md)「所持リソース設計(items / gems)」参照)。スカウトの紹介コスト例(`150`/回)の
2回分に相当し、初回起動時点で最低限スカウトを試せるようにする。

### 対戦勝利報酬

`/internal/battle/result`の処理内で、`winner_id`のプレイヤーに固定量のgemsを付与する。

| 項目 | 値 |
|---|---|
| 勝利報酬 | `50` gems(固定) |
| 敗北報酬 | なし(`0`) |

- 引き分けの概念は無い(`battle_matches`は`status = 'finished'`になる際`winner_id`が
  必須のため、決着が付かない対戦は考慮しない)
- 報酬額はマスタデータ化せず、Rust側の定数(`BATTLE_WIN_REWARD_GEMS`)として持つ
  (`scout_banners.rate_table`のように運用中に調整する想定がないため)
- `player_items`(`item_id: 1`、gems)の`quantity`への加算(行が無ければ作成するUPSERT)は、`battle_matches`のステータス更新・
  `battle_turns`のINSERTと同一トランザクションで行う
- 「いつ・いくら付与したか」を記録する専用の履歴テーブルは設けない。
  `battle_matches.winner_id`と固定額から常に再計算できるため、監査目的の別テーブルは
  過剰と判断

### 未確定の論点

- 敗者への参加賞(少額のgems)は入れない方針。プレイ継続のインセンティブが弱ければ
  将来的に検討する
- 報酬額(初期`300`・勝利`50`)とスカウトコスト(例`150`/回)のバランスは仮値。
  実際のプレイテストで調整が必要
- 不正対策(複数アカウントでの馴れ合いマッチによるgems稼ぎ)は本プロジェクトの規模では
  対象外

## MagicOnion Hub設計(C#側)

- `JoinAsync`で`battleToken`を検証 → 対戦グループにアタッチ
- ダメージ計算等の判定は`ConcurrentDictionary<matchId, BattleState>`等のインメモリ状態で
  確定(クライアントには結果のみ送る、チート対策)
- 終了時に`OnBattleEnd`をブロードキャストしつつ、`/internal/battle/result`へ結果POST
- `JoinResult`/`MoveRequest`/`BattleStartPayload`等のDTOは「IBattleConnection / Payload定義」
  (上記)で定義したものと同じ形を使う。`IBattleConnection`はこのHubの契約に対になるように
  設計してある

### プロジェクト構成

- `dotnet new web`(ASP.NET Core Empty)+`MagicOnion.Server`のNuGetパッケージ追加、という
  公式Getting Started/[ChatAppサンプル](https://github.com/Cysharp/MagicOnion/tree/main/samples/ChatApp)
  と同じ最小構成で立ち上げる(専用テンプレートは無い)。`Program.cs`はKestrelをHTTP/2のみに
  設定して`AddMagicOnion()`/`MapMagicOnionService()`を呼ぶ数行で済む
- Docker化は行わない(Rust APIサーバーと同じ判断。Windows上でのビルド速度・デバッグの
  しやすさを優先。デプロイ方式を決める段階で改めて検討する。Rust側の同判断は
  [Shared/docs/progress.md](../progress.md) S-2参照)

### 再接続時の盤面復元

- 切断検知は`StreamingHubBase.OnDisconnected()`のoverrideで行う。切断時に対戦相手へ
  `OnOpponentDisconnected`をブロードキャストし、`CancellationTokenSource`+
  `Task.Delay(猶予秒数)`で猶予タイマーを開始する
- 猶予時間内に再接続(=同じ`battleToken`/`matchId`で`JoinAsync`を再実行)があれば
  タイマーをキャンセルし、`OnOpponentReconnected`をブロードキャストする。このとき
  専用の再同期メソッドは設けず、`BattleState`の現在値から組み立てた`BattleStartPayload`を
  再接続した接続者にだけ`OnMatchStart`として再送することで盤面を復元する
  (`BattlePresenter`は`OnMatchStart`受信時に`SelfParty`/`OpponentParty`を初期化する
  実装のため、そのまま復元処理として機能する。`IBattleHub`/`IBattleConnection`の契約
  変更は不要)
- 猶予時間内に再接続できなければタイマー発火でforfeit処理(`OnBattleEnd`+
  `/internal/battle/result`)を行う
- `battleToken`の有効期限(30秒)は猶予時間(60秒)より短いため、**再接続に限り**期限切れ
  (署名・claimsは正当)のトークンを受け付ける。新規参加には有効期限内のトークンを要求する
- 猶予中は切断中の側のターンタイマーも止め、再接続時に30秒からやり直す(再接続者には
  `OnMatchStart`で`TurnTimeLimitSeconds`が届くが、相手側のローカル表示タイマーとはずれ得る)

```csharp
public interface IBattleHub : IStreamingHub<IBattleHub, IBattleHubReceiver>
{
    Task<JoinResult> JoinAsync(string battleToken, string matchId);
    Task SubmitSelectionAsync(string[] playerPachimonIds);  // 常に3体固定、クライアントが自動送信
    Task SubmitMoveAsync(MoveRequest move);
    Task SwitchAsync(int partySlot);
    Task ForfeitAsync();
}

public interface IBattleHubReceiver
{
    void OnMatchStart(BattleStartPayload payload);   // 選出が揃ってから発火
    void OnTurnResult(TurnResultPayload payload);
    void OnBattleEnd(BattleEndPayload payload);
    void OnOpponentDisconnected();   // IBattleConnectionの同名イベントに対応
    void OnOpponentReconnected();
}
```

```csharp
public enum BattlePhase
{
    WaitingForJoin,
    Selecting,      // 実際に使う、両者の選出待ち
    InProgress,
    Finished
}
```

- サーバー側の`BattleState`は選出結果(2プレイヤー分)を保持し、両者揃った時点で
  `InProgress`に遷移させる
- `battle_turns`の行動対象は選出済み3体の範囲内に限定される(ダメージ計算・交代ロジック
  もこの3体で完結)

## パチモン選出(自動選出)

マッチング成立後、実際にバトルへ出す3体を選ぶ「選出」フェーズを実装する。UI(手動選択画面)
は作らず、クライアントが接続直後に**party_slot順で先頭3体のplayer_pachimon_idを自動送信**
する形にする。選出という仕組み自体は本物として実装しておき、将来UIを追加する際は
自動送信部分を差し替えるだけで済むようにする。

**フロー**

```
1. マッチ成立 → 両クライアントがMagicOnionに接続してJoinAsync
2. 各クライアントは接続直後、party_slot順で先頭3体のplayer_pachimon_idを自動的にSubmitSelectionAsyncで送信
3. サーバーは両プレイヤーの選出が揃うまで待機(BattlePhase.Selecting)
4. 揃ったらBattlePhase.InProgressへ遷移、OnMatchStartで両者に選出内容を通知してバトル開始
```

**選出の制限時間**

- 両者の`JoinAsync`がそろった時点から**2分**以内に選出しなかった側の敗北(`BattleEndReason.Forfeit`)
- 両者とも選出しなかった場合は勝者なし(`OnBattleEnd`の`WinnerId`は空文字)。`/internal/battle/result`にも
  `winnerId`を空文字で報告し、`battle_matches`は`aborted`になる
- 自動選出(クライアントが接続直後に送信)が前提なので通常は発生しない。クライアントの不具合等で
  対戦が選出待ちのまま永久に残らないようにするための上限

## バトルコアロジック(ダメージ計算・命中率)

本家ポケモンの基本式から、天候・フィールド・持ち物・特性・急所ランク変動・命中/回避ランク
変動・優先度技・性格補正を除いたミニマム版。

### 1ターンの処理順

概要: 両プレイヤーの行動(技 or 交代 or 非行動)を受け取り、行動優先度([行動順序](#行動順序)参照)
に従って順に処理(命中判定→ダメージ計算→HP減算→瀕死チェック)し、両者の行動が終わったら
`turn_result`をブロードキャストする。強制交代チェック等を含む正確な処理順は「[内部構造
(Section / Event / EventHandler)](#内部構造-section--event--eventhandler)」のSection階層を
正とする(このセクションは概要のみ)。

### 行動順序

- 交代は必ず技より先に処理
- 技同士は実効`speed`が高い方が先、同値ならランダム
- 優先度技(でんこうせっか等)は実装しない(純粋に素早さ比較のみ)

### 命中判定

技マスタの`accuracy`(固定値)を使った判定は実装する。

```
乱数(1〜100) <= move.accuracy なら命中
```

命中/回避ランクを変動させる効果(追加効果に該当)は実装しない。

### ダメージ計算式

```
damage = floor(floor(floor(2 * level / 5 + 2) * base_power * A / D) / 50 + 2)
         * STAB * TypeEffectiveness * Critical * Random
```

- `level` = 全パチモン共通の固定値(`Atlas.BattleCore.BattleConstants.FixedLevel = 50`)。
  このゲームは経験値によるレベルアップを持たないため、個体ごとに変動しない
- `A` = 攻撃側の実効ステータス(物理技ならatk、特殊技ならspatk)
- `D` = 防御側の実効ステータス(物理技ならdef、特殊技ならspdef)
- `STAB` = 技の`move_type`が攻撃側の`primary_type`または`secondary_type`と一致するなら
  `1.5`、それ以外`1.0`
- `TypeEffectiveness` = `type_chart`から取得した`effectiveness`(ENUM)を倍率に変換し、
  複合タイプ(secondary_type)は2回引いて掛け合わせる。`Atlas.BattleCore`は`ITypeChart`
  (`GetMultiplier(attackType, defendType)`)という抽象だけを持ち、実装(マスタからの読み込み)
  は呼び出し側(Client Mock / バトルサーバー)が注入する(`IRandomSource`と同じ注入パターン)
- `Critical` = 発生率固定(1/16程度)、発生時ダメージ`1.5`倍。ランク変動要素はなし
- `Random` = `0.85〜1.00`の乱数

`category = 'status'`(状態技)は追加効果を実装しない都合上、現状は効果なし。初期習得技
(`is_initial`)は物理/特殊技のみで構成する運用とする。

### PP消費

技を選択した時点で、命中/外れ/状態技に関わらず対象の技のPPを1消費する(本家ポケモン準拠、
実装は`PachimonState.ConsumeMovePp`/[PachimonState.cs](../../BattleCore/Runtime/PachimonState.cs)参照)。
PPが0の技が渡された場合は`ArgumentException`を投げる。呼び出し側がPP0の技を渡さないことを
前提にした設計であり、その防止策(UIでの選択制限)は下記「[技選択時のPP制限(UI側)]
(#技選択時のpp制限ui側)」で扱う。

### 実効ステータス計算

個体値(IV)の概念は持たない(ポケモンチャンピオンズ準拠で廃止)。

```
HP以外 = floor((2 * base + floor(EV/4)) * level / 100) + 5
HP     = floor((2 * base + floor(EV/4)) * level / 100) + level + 10
```

`level`はダメージ計算式と同じく固定値50。`effort_values`は現状全て0。性格(nature)補正は
本家にあるがマスタ設計に存在しないためなし。

### 瀕死・交代

- HPが0になったら瀕死、行動不能
- 選出済み3体全員が瀕死になった時点で敗北、`battle_end`
- 瀕死時は次ターン開始前に強制交代を要求(未交代ならターンスキップ)

クライアントUI:

- 交代ボタン→`SwitchSelectModal`で選出3体(名前・HP・状態)から交代先を選ぶ。場に出ている
  パチモンと瀕死のパチモンは選べない(`Atlas.BattleCore`は瀕死への交代は例外、場のパチモン
  自身への交代は検証しないため、UI側で防ぐ)
- 強制交代(`PlayersRequiringForcedSwitch`に自分が含まれる)は同じModalを「やめる」無しで出し、
  生存が1体だけでも自動では選ばない。選択中は技を送れない
- 行動(技・交代)を送ってから`turn_result`が届くまでは技・交代ボタンを押せない(投了は常に可能)
- 交代先の選択中に`turn_result`(タイムアウトによるスキップ等)や`battle_end`が届いた場合、
  Modalを閉じる。強制交代が続いていれば改めて出す

### ターンタイムアウト

- 各ターンに制限時間を設ける。**30秒**で確定(仮値、定数を変えるだけで後から調整可能)
- 制限時間内に行動(技 or 交代)が送信されなかった場合、そのプレイヤーは当該ターンの
  行動権を失う(何もしない扱いでターンスキップ)。デフォルト行動の自動送信は行わない
- 相手側が制限時間内に行動していれば、相手の行動のみ通常通り処理される

### 放置

- ターンタイムアウトによる非行動が**3ターン連続**した側の敗北(`BattleEndReason.Forfeit`)。
  1回でも行動すれば回数は0に戻る
- 両者が同じターンに3ターン連続に達した場合は勝者なし(`WinnerId`は空文字、`battle_matches`は`aborted`。
  「選出の制限時間」と同じ扱い)
- 選出の制限時間・放置のどちらも、専用の`BattleEndReason`は追加せず`Forfeit`で表す
  (`BattleEndReason`はUnityと共有する`Atlas.BattleCore`の型のため、値の追加を避けた)

### 切断・再接続

- MagicOnionの接続断を検知した場合、`BattleState`はすぐには破棄せず、猶予時間
  **60秒**(仮値、定数を変えるだけで後から調整可能)は対戦を一時停止して再接続を待つ
- 猶予時間内に再接続できた場合、サーバー側が保持する`BattleState`から現在の
  盤面情報を再送し、対戦を続行する。実装方式は「MagicOnion Hub設計(C#側)」の
  「再接続時の盤面復元」参照
- 猶予時間内に再接続できなかった場合、切断側の敗北として`battle_end`で終了する
  (forfeit扱い)
- 猶予時間中は両プレイヤーとも新たな行動を受け付けない(片方が復帰待ちの間、
  もう一方だけターンが進行することはない)

## DB設計

### battle_matches

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `match_id` | CHAR(26) | PRIMARY KEY | ULID |
| `player1_id` / `player2_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | |
| `status` | ENUM('matching','in_progress','finished','aborted') | NOT NULL | `aborted`は勝者なしで終了(勝者なしの結果報告、または結果報告が無いまま打ち切り) |
| `winner_id` | CHAR(26) | NULL可, FOREIGN KEY → `players.player_id` | `finished`の場合のみ設定 |
| `player1_selected_pachimon` / `player2_selected_pachimon` | JSON | NULL可 | 選出された`player_pachimon_id`3体分(自動選出でも実データとして記録)。結果報告時に必ず設定する(未参加側は`[]`)ため、NULLなら未報告 |
| `started_at` / `ended_at` | DATETIME(3) | NULL可 | |

### battle_turns(対戦ログ)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `turn_id` | CHAR(26) | PRIMARY KEY | ULID |
| `match_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `battle_matches.match_id` | |
| `turn_number` | INT | NOT NULL | |
| `player_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | 行動者 |
| `action_data` | JSON | NOT NULL | |
| `result_data` | JSON | NOT NULL | |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | |

補足:

- `battle_server_id`はサーバー1台構成のため不要と判断し省略
- マッチング待機列(`matchmaking_queue`)はDB永続化せず、Rustプロセスのメモリ(`AppState`の`Mutex`)で管理
- `status`の`'matching'`は現状使わない(待機中はDBに行を作らず、ペア成立時点で`'in_progress'`として作成する)

## 将来拡張・未確定の論点

- 技の入れ替えUI・選択ロジックの高度化(候補が5件以上ある場合の絞り込み等) — スキーマは対応済み
- 努力値(64ポイント)の自由配分機能とその上限チェック — `effort_values`カラムは用意済み、配分ロジック・UIは未実装
