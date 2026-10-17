# Atlas Unity Client

Unity 6(IL2CPP前提)のクライアント。プロジェクト全体の共通ルール(コメントにドキュメントへの参照を書かない等)は
[ルートのCLAUDE.md](../../CLAUDE.md)。構成と設計(なぜそうしたか)はwikiに書いている。

| 知りたいこと | 読む場所 |
|---|---|
| シーン・VContainerのスコープ・起動の流れ | [シーンとスコープ](../../docs/wiki/client/scenes-and-scopes.md) |
| View / Presenter・画面一覧 | [画面の構成](../../docs/wiki/client/screens.md) |
| Push/Pop・遷移の直列化・シーンの切り替え | [画面遷移](../../docs/wiki/client/navigation.md) |
| Service・Repository・`player_diff`の反映・マッチング | [サービスと所持データ](../../docs/wiki/client/services-and-player-data.md) |
| asmdefの構成とフォルダ | [アセンブリとレイヤー](../../docs/wiki/client/assemblies.md) |
| バトル画面の流れ・状態・演出 | [バトル画面](../../docs/wiki/client/battle-screen.md) |
| 通信エラーのダイアログ | [通信エラーのダイアログ](../../docs/wiki/client/error-dialog.md) |
| テスト・Mock対戦 | [テストとサーバー無しでの動作](../../docs/wiki/client/testing.md) |

## 構成の上で守ること

### レイヤーと依存

- オニオンアーキテクチャ。依存は外側から内側への一方向だけにする(asmdefの参照で強制している)。
  通信・保存のインターフェースは内側(`Atlas.Application`・`Atlas.Domain`)に置き、`Atlas.Infrastructure`が実装する
- **`Atlas.Presentation`は`Atlas.Infrastructure`も`Atlas.DI`も参照しない**。実装を組み立てるのは`Atlas.DI`(構成ルート)だけ
- DIの登録は`RootLifetimeScope`と、各アセンブリの`_DIExtensions.cs`の拡張メソッドに置く。シーン単位の`LifetimeScope`は`Atlas.DI`に置く

### 画面

- 「Viewのユーザー操作 → Presenterが購読 → Serviceを呼ぶ → ViewDtoを作る → Viewを更新」の一方向にする
- Viewにインターフェースは作らない(PresenterとViewは1対1の組で、差し替える場面が無いため)。ViewはPresenterを知らない
- Page/Modalのprefabに`LifetimeScope`は置かない(子スコープは`ScreenNavigator`が作る)
- Home→Battleの受け渡しはRoot常駐の`BattleEntryStore`を通す。シーンの切り替えは`ISceneNavigator`を使う
- 同じ画面の中の通知はR3の`Observable`を直接購読する。どの画面からとも決まらない通知だけSupplementの`IMessageBroker`を使う

### 通信と所持データ

- APIサーバーの呼び出しはServiceの実装(`Atlas.Infrastructure`)が、`.proto`から生成したラッパー(`XxxApi`)を呼んで行う。
  PresenterはServiceのインターフェース越しにだけ通信する
- 通信が失敗したとき、ダイアログ・タイトルへ戻る等は基盤の`ApiCore`が済ませている。Serviceは成否(または結果の型)だけを返し、
  呼び出し側は成否を見て抜けるだけにする。**`try/catch`で通信のエラーを処理しない**。画面で自分で処理したいエラーだけ、
  ラッパーの`handledCodes`に渡す
- `player_diff`の反映は`ApiCore`が`PlayerDiffApplier`で行う。Serviceは`player_diff`を扱わない。新しい所持データの種別を足すときは、
  `XxxDiffApplier`とRepositoryを1組足す(汎用のDiff型・Repositoryは作らない)
- Repositoryはメモリに持つだけにする(ディスクに保存しない)
- 生成コードは`Infrastructure/Grpc/Generated/`(asmdef `Atlas.Api.V1`。手で編集しない)。`.proto`・`Shared/errors.toml`を
  変えたら、リポジトリのルートで`make proto-gen`・`make errors-gen`を実行する
- 業務エラーを足したら、文言を`Presentation/Common/ErrorMessages.cs`に足す
- テストでサーバーの代わりにするときは、Serviceを差し替えず、`CallInvoker`を基盤の`FakeCallInvoker`
  (偽のサーバー`FakeServer`を登録したもの)に差し替える

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
  /// design.md「6.6 通信と所持データ」参照。すぐに成立したマッチを返すだけの実装。
  /// </summary>
  public sealed class MockBattleMatchMaker : IBattleMatchMaker { ... }

  // Good: コメント自体を書かない(クラス名・戻り値で自明なため)
  public sealed class MockBattleMatchMaker : IBattleMatchMaker { ... }
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
- Service・Repositoryのメソッドは`CancellationToken`を受け取らない(RootのSingletonで、画面の表示期間に
  結び付かないため)。対戦の`IBattleConnection`はセッション単位のため例外

### アセット(prefab・シーン)

- **prefab・シーン・アセットのYAMLを直接書き換えない**。Unityのエディタ上で`unity command eval`(Unity CLI)から
  `PrefabUtility`・`SerializedObject`等のAPIで編集する
- **Prefabモードで開いているprefabを、別に`PrefabUtility.LoadPrefabContents`で読み込んで保存しない**。
  開いているPrefabモード側の内容で上書きされる。開いている場合は、未保存の変更が無いことを確かめてから
  `PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot`を編集して保存する
- 色は`PaletteColor`(`UiPalette`の役割)で付ける。Imageの色を直接変えても`OnEnable`で上書きされる
- `AddressDefinition.cs`はAddressablesから自動生成する。Page/Modalのprefabを足したら生成し直す
- フォントは文字を焼き込んだStaticアセット。新しい文字(第2水準漢字等)を使ったら`Tools > Bake Static Font`で焼き直す

### 確認

- Editorの操作はUnity CLI(`unity`)で行う。Editor側は`com.unity.pipeline`パッケージが受ける。
  使えるコマンドは`unity command`で一覧できる
- C#を変えたら`unity recompile --strict`でエラー・警告が0であることを確かめる。EditMode/PlayModeのテスト
  (`unity command run_tests`)は頼まれたときだけ回す
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
