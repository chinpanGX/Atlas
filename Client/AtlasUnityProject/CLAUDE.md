# Atlas Unity Client

Unity 6(IL2CPP前提)のクライアント。プロジェクト全体のアーキテクチャと共通ルール(コメントにドキュメントへの
参照を書かない等)は[ルートのCLAUDE.md](../../CLAUDE.md)。

## アーキテクチャ

### シーンとスコープ

```
Bootstrap(起動シーン。一度もアンロードしない)
  RootLifetimeScope: マスタの読み込み、サインイン、Connection・Repository・ServiceのDI登録
    ▼
Home / Battle(Addressables。ISceneNavigatorで切り替える)
  HomeLifetimeScope / BattleLifetimeScope: USNのコンテナとIScreenNavigator
    ▼
Push単位の子スコープ(ScreenNavigatorが画面を開くたびに作り、閉じると破棄する)
  View・ViewDto・Presenter
```

- DIはVContainer。スコープは Root(アプリの生存期間)→ シーン(`FindParent()`でRootを直接親にする)→ 画面
- シーンの中の画面遷移はUnityScreenNavigator(USN)のフォークのPage/Modal。Presenterの型でPushし、
  フォークの`ScreenNavigator`が子スコープでPresenterを解決する
- Home→Battleの受け渡し(マッチ情報)はRoot常駐の`BattleEntryStore`を通す(シーンを切り替えるとHomeのスコープは破棄されるため)
- シーンの切り替えは`TransitionAwareSceneNavigator`が全コンテナの遷移の完了を待ってから行う(遷移アニメーション中に
  画面を破棄すると例外になるため)

### 画面(View / Presenter)

「Viewのユーザー操作 → Presenterが購読 → Service(またはConnection)を呼ぶ → ViewDtoを作る → Viewを更新」の一方向。

| 役割 | 型 | 内容 |
|---|---|---|
| View | `XxxPage`/`XxxModal`(USNの`Page`/`Modal`) | ボタン等を`Observable`で公開し、`Refresh(dto)`で表示に反映するだけ |
| Presenter | `XxxPresenter`(フォークの`IPresenter`、引数を受け取る画面は`IScreenWithArgs<XxxViewDto>`) | 具象のViewとService等をコンストラクタで受け取り、`InitializeAsync`でViewを購読する |
| 表示データ | `XxxViewDto` | Push時の入力と表示データを兼ねる |
| Pop結果 | `XxxResult` | Presenterの`CompleteAsync`で返し、Push元が`WaitForPopAsync`で受け取る |

- Viewにインターフェースは作らない(PresenterとViewは1対1の組で、差し替える場面が無いため)
- Presenterには`[AssetAddress(AddressDefinition.Xxx)]`でprefabのアドレスを付ける。prefabのアドレスはViewのクラス名と同じにする
- 同じ画面の中の通知はR3の`Observable`を直接購読する。どの画面からとも決まらない通知だけSupplementの`IMessageBroker`を使う

### 通信と所持データ

| 抽象 | 置き場所 | 内容 |
|---|---|---|
| `IXxxConnection` | `Atlas.Application` | サーバーのAPIの呼び出し。Mock/Realの切り替えはこの単位 |
| `IXxxRepository` | `Atlas.Domain` | 手元の所持データ。`playerDiff`のリソース種別ごとに1つ。メモリに持つだけ(起動のたびにサインインで取り直す) |
| `IXxxService` | `Atlas.Application` | Presenterが使う処理。実装は1つ |
| `IBattleConnection` | `Atlas.Domain` | 対戦の接続(`RealtimeBattleConnection`/`MockBattleConnection`)。`IBattleConnectionFactory`で対戦ごとに作る |

- `playerDiff`を返すAPIを呼ぶConnectionは、受け取った直後に`IPlayerDiffApplier`で各Repositoryへ反映する。
  Serviceは`playerDiff`を知らない
- 要認証APIの呼び出しは`AccessTokenRefresher.SendAsync`で包む(期限前の再認証・401時の1回だけの再送)
- REST通信のDTO・クライアントは`Infrastructure/Api/Generated/`(`api-codegen`の生成物。手で編集しない)。
  手書きのConnectionが生成DTOをDomain/Applicationの型へ詰め替える

### アセンブリ

| asmdef | 置くもの |
|---|---|
| `Atlas.Domain` | Entity、`IXxxRepository`、`IBattleConnection`とその型 |
| `Atlas.Application` | `IXxxService`、`IXxxConnection`、戻り値の型、`BattleEntryStore`、`AddressDefinition`(生成物) |
| `Atlas.Infrastructure` | Serviceの実装、端末に保存するRepository、`MasterDataService` |
| `Atlas.Infrastructure.Api` / `.Mock` / `.Realtime` | REST通信(生成物と手書きのConnection・Repository)/ Mock / MagicOnion |
| `Atlas.Navigation` | `TransitionAwareSceneNavigator` |
| `Atlas.Presentation` | View・Presenter・ViewDto(画面単位の型だけ) |
| `Atlas.DI` | シーン単位の`LifetimeScope`すべて(構成ルート。全レイヤーに依存してよい唯一のアセンブリ) |
| `Atlas.MasterData` | マスターデータの生成物とローダー(Source Generatorの都合で1アセンブリにまとめる) |

- 依存は常に`Atlas.DI`→各レイヤーの一方向。`Atlas.Presentation`は`Atlas.Infrastructure`も`Atlas.DI`も参照しない
- `Atlas.BattleCore`・`Atlas.BattleContracts`はリポジトリの`Shared/`をローカルパッケージとして参照している

### フォルダ

`Assets/Scripts/Presentation/{機能名}/`にPageを置き、Modalは`{機能名}/{Modal名}/`のサブフォルダにする(namespaceは
機能名のまま)。Pageの部品が多い場合は`Views/`にまとめる。機能に属さない共通の型は`Common/`。prefabは
`Assets/Addressables/Views/{機能名}/`の直下(Modalもサブフォルダを切らない)、機能をまたぐ部品は`Views/Parts/`。

## コーディングルール

### C#命名規約

- **メンバー変数(フィールド)にアンダースコア接頭辞(`_camelCase`)を付けない**。Rider標準の
  コードスタイルに合わせ、フィールドも通常のパラメータ・ローカル変数と同じ`camelCase`で書く
  (例: `private readonly IPlayerAccountService playerService;`であって
  `private readonly IPlayerAccountService _playerService;`ではない)
- コンストラクタ引数とフィールド名が同じになる場合は`this.`で明示的に区別する

  ```csharp
  public sealed class HomePresenter
  {
      private readonly IPlayerAccountService playerService;

      public HomePresenter(IPlayerAccountService playerService)
      {
          this.playerService = playerService;
      }
  }
  ```

- ラムダの未使用引数の`_`(discard)はこの規約の対象外(フィールドではないため、そのままでよい)
- private の`const`・`static readonly`は`PascalCase`。これらの命名ルールはリポジトリ直下の`.editorconfig`にあり、
  Riderで警告になる(Unityのコンパイルでは検査されない)
- 画面ごとの型は画面名をプレフィックスにそろえる(`XxxPage`/`XxxModal`・`XxxPresenter`・`XxxViewDto`・`XxxResult`)

### ファイル名

- **ファイル名は、そのファイルで定義する主要な型(class/struct/interface/record/enum)の名前と
  一致させる**(例: `PachimonEntity`は`PachimonEntity.cs`であって`Pachimon.cs`ではない)
- 1ファイルに複数の型を置く場合(実装クラスとそのインターフェース等)も、主となる型の名前をファイル名にする
  (例: `PlayerDiffApplier.cs`に`IPlayerDiffApplier`と`PlayerDiffApplier`)
- Unityで型名を変更したときは、`.cs`と同名の`.meta`も一緒にリネームする(GUIDを維持するため)

### コメント

- ドキュメントへの参照(「design.md「〇〇」参照」等)は書かない。クラス名・メソッド名を読めばわかることを
  そのまま繰り返すだけのコメントも書かない
- コメントを書くなら、コードを読むだけでは伝わらない「概要」や「なぜそうなっているか」が
  一目でわかる内容にする。自明な実装(例: 固定値を返すだけのMock実装)にはコメント自体
  不要な場合が多い

  ```csharp
  // Bad: ドキュメント参照、または自明な内容の繰り返し
  /// <summary>
  /// design.md「6.6 通信と所持データ」参照。インメモリの擬似データを返すだけの実装。
  /// </summary>
  public sealed class MockPlayerConnection : IPlayerConnection { ... }

  // Good: コメント自体を書かない(クラス名・戻り値で自明なため)
  public sealed class MockPlayerConnection : IPlayerConnection { ... }
  ```

### 不要な`async`/`await`

- 実際には非同期で待つ理由が無いのに、`async`/`await`や`UniTask.Delay`等で人工的な待機を
  入れない。同期的に値を返せるなら`UniTask.FromResult`等をそのまま返す

  ```csharp
  // Bad: 待つ理由が無いのにawaitしている
  public async UniTask<PlayerData> GetMeAsync()
  {
      await UniTask.Delay(200);
      return new PlayerData("mock-player-id", "プレイヤー", 300);
  }

  // Good
  public UniTask<PlayerData> GetMeAsync()
  {
      return UniTask.FromResult(new PlayerData("mock-player-id", "プレイヤー", 300));
  }
  ```

### Presenter・画面遷移

- 通信のように時間のかかる処理は`InitializeAsync`で待たない(待つ間、開くアニメーションが始まらない)。
  開始だけして先に画面を開く。`InitializeAsync`の中でシーンの切り替えを待つとデッドロックする
- Presenterは`Dispose`で購読を解放するだけでよい(子スコープの破棄で呼ばれる。自分で呼ばない)
- 同じ画面を二重にPopしない。ボタンは`Take(1)`、通信を待つ間の連打は`AwaitOperation.Drop`で捨てる。
  Push元からも閉じる画面は、最初の結果を優先して2回目以降は進行中のPopを待つだけの`CloseAsync`を持たせる
- Service・Connection・Repositoryのメソッドは`CancellationToken`を受け取らない(RootのSingletonで、画面の表示期間に
  結び付かないため)。対戦の`IBattleConnection`はセッション単位のため例外

### アセット(prefab・シーン)

- **prefab・シーン・アセットのYAMLを直接書き換えない**。Unityのエディタ上で`uloop execute-dynamic-code`から
  `PrefabUtility`・`SerializedObject`等のAPIで編集する
- **Prefabモードで開いているprefabを、別に`PrefabUtility.LoadPrefabContents`で読み込んで保存しない**。
  開いているPrefabモード側の内容で上書きされる。開いている場合は、未保存の変更が無いことを確かめてから
  `PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot`を編集して保存する
- 色は`PaletteColor`(`UiPalette`の役割)で付ける。Imageの色を直接変えても`OnEnable`で上書きされる
- `AddressDefinition.cs`はAddressablesから自動生成する。Page/Modalのprefabを足したら生成し直す
- フォントは文字を焼き込んだStaticアセット。新しい文字(第2水準漢字等)を使ったら`Tools > Bake Static Font`で焼き直す

### 確認

- C#を変えたら`uloop compile`でエラー・警告が0であることを確かめる。EditMode/PlayModeのテスト(`uloop run-tests`)は
  頼まれたときだけ回す
- `Shared/BattleCore`・`Shared/BattleContracts`の中で`dotnet build`しない(`bin/`・`obj/`ができ、UnityがそのDLLを
  取り込んでCS1704になる)

### `*LifetimeScope.cs`を新規作成するとき(エディタ外からの作成)

VContainerの`ScriptTemplateProcessor`(`Editor/ScriptTemplateModifier.cs`)は、`.meta`がまだ無い
`*LifetimeScope.cs`をUnityが検知するたびに、中身を空のテンプレート(`Configure`が空の
`LifetimeScope`派生クラス)へ強制的に上書きする。「Unityのメニューから新規作成したときだけ」用の
機能だが、判定条件は`.meta`の有無だけなので、エディタ外(Write等)で書いた`*LifetimeScope.cs`にも
無条件で効いてしまう。

`VContainerSettings.DisableScriptModifier`で無効化できるように見えるが、**Editモードでは効かない**
(`VContainerSettings.Instance`は`OnEnable`内の`Application.isPlaying`ガードのせいでPlayモード中しか
設定されない。実機検証済み)。この設定を有効にしても、新規スクリプト作成が起きるEditモードでは
意味が無い。

**対策**: `*LifetimeScope.cs`を新規作成するときは、`.cs`本体と同時に`.meta`も自分で作成する
(Unityにまだ存在しない`.meta`を生成させない)。`.meta`が既にある状態でインポートされる場合、
Unityは「新規アセット」とみなさず`OnWillCreateAsset`自体を呼ばないため、上書きは発生しない
(実機検証済み)。

```yaml
fileFormatVersion: 2
guid: <32桁の16進数、例: uuidのhexで生成>
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```
