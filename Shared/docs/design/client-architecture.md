# クライアントアーキテクチャ設計(画面遷移・DI・コア進行ロジックの疎結合化)

Unity Client側の「View⇔ロジック」を繋ぐ土台の設計。個別画面の見た目・UseCase自体は
対象外で、各画面を実装する際にここで定義する型に沿って追加していく前提。全体構成・
命名規則は[architecture.md](architecture.md)、バトル固有の`IBattleConnection`定義は
[battle.md](battle.md)を参照。

## 採用ライブラリ

- 画面遷移: [UnityScreenNavigator](https://github.com/Haruma-K/UnityScreenNavigator)(USN、MIT
  License)。Push/Popによるスタック型画面遷移・モーダル・遷移アニメーション・ライフサイクル
  イベントを提供する。このアプリはPage間のフロー制御が単純(タイトル→ホーム→各機能→対戦、
  程度)で済むため、フロー概念(名前付きルート・トランザクション等)を持つような大掛かりな
  自作フレームワークは過剰と判断し、シンプルなPush/Pop型のOSSを採用する
- DI: VContainer(既存、[architecture.md](architecture.md)「クライアント利用ライブラリ」参照)
- View↔Presenter間のリアクティブ購読: R3(未導入、[battle.md](battle.md)「UI層との連携」参照)。
  **同じ画面内のView→Presenterの通知にはこれのみを使う**(画面をまたぐ通知との使い分けは
  下記「画面をまたぐ通知(IMessageBroker)」参照)
- 画面をまたぐ通知: Supplementの`Supplement.Core.IMessageBroker`
  (実装は`Supplement.ZeroMessenger.GlobalMessageBroker`、[ZeroMessenger](https://github.com/AnnulusGames/ZeroMessenger)
  をラップしたもの)を**グローバル用途限定**で導入(詳細は下記「画面をまたぐ通知
  (IMessageBroker)」参照)。MessagePipeは検討したが、Supplementに既に同種の仕組みがあり
  二重導入になるため不採用。battle.mdの「追加のpub/subライブラリは導入しない」という記述は、
  この限定的な用途に絞った上で更新する

## シーン構成(Scene / Addressables)

`Bootstrap` / `Home` / `Battle`の3シーン構成にする。バトルとアウトゲームは別シーンとして
分離し、起動処理専用のシーンを独立させる。画面遷移を2階層に分ける:

- **Scene単位の遷移**(Bootstrap→Home、Home⇔Battle): Supplementの`ISceneLoader`
  (Addressables経由、[ISceneLoader.cs](../../../Supplement/Assets/Supplement/Loader/Abstractions/ISceneLoader.cs)参照)で行う
- **Scene内の画面遷移**(Home内のタイトル/パーティ編成/スカウト等、Battle内の対戦画面/
  投了確認等): USNのPage/Modalで行う

```
Bootstrap(Build Settingsの起動シーン、Addressables対象外)
  RootLifetimeScope(VContainer) -- アプリ生存期間中ずっと存在
  最小限のロードUI(スプラッシュ/プログレス表示のみ、USNは使わない)
  役割: MasterData読み込み、デバイス認証・サインイン(ISignInService)、各IXxxConnection
        (Mock/Real)・IXxxRepository・IXxxServiceのDI登録。完了後にHomeシーンへ遷移する
    │ ISceneLoader.ChangeScene("Home", additive: true, ...)
    ▼
Home(Addressables管理)
  UICamera / PageContainer / ModalContainer(USN)
  HomeLifetimeScope(VContainer、RootLifetimeScopeの子)
  タイトル・ホーム・パーティ編成・スカウト・チャット等、アウトゲームの全画面をUSNの
  Page/Modalとして持つ
    │ マッチング成立 → ISceneLoader.ChangeScene("Battle", additive: true, ...)
    ▼
Battle(Addressables管理)
  UICamera / PageContainer / ModalContainer(USN)
  BattleLifetimeScope(VContainer、RootLifetimeScopeの子)
  対戦画面のPage、投了確認等のModal
    │ 対戦終了 → ISceneLoader.ChangeScene("Home", additive: true, ...)
    ▼
  (Homeへ戻る)
```

- Bootstrapは`ISceneLoader.ChangeScene`の対象にせず、常にロードされたままにする(Home/Battleを
  上に重ねてロードする側)。これにより`RootLifetimeScope`は`DontDestroyOnLoad`等の特別な仕組み
  なしに、「Bootstrapシーン自体が一度もUnloadされない」という性質だけで生存させられる
- Home⇔Battleの切り替えは、前のシーンをUnloadしてから次をLoadする想定(両方を同時にロードした
  ままにしない)
- Page/Modal prefabはAddressablesで管理し、`Assets/Addressables/Views/{機能名}/`配下に配置する
  (シーン自体のAddressables化とは別の対象)。Addressables管理のシーン(Home/Battle)は
  `Assets/Addressables/Scenes/`に置き、Bootstrap(Build Settings登録)は`Assets/Scenes/`に残す
- `SheetContainer`(タブ的な非スタックUI)は、現時点で必要な画面が無いため導入しない。
  必要になった画面が出てから追加する

## 責務分担(Page / View / Presenter / Service)とデータフロー

「Viewのユーザーイベント→PresenterがSubscribe→Serviceを呼ぶ→結果からDTOを作る→
Pageをリフレッシュする」という一方向のフローに統一する。**同じ画面内のView→Presenter通知に
pub/subバスは使わず**、R3のObservableによる直接購読のみで完結させる。画面をまたぐ通知だけ
Supplementの`IMessageBroker`を限定的に使う(詳細は下記「画面をまたぐ通知(IMessageBroker)」
参照。battle.mdの「追加のpub/subライブラリは導入しない」はこの限定用途を踏まえて更新する)。

> この節の設計は、[Mirrativ社のVContainerアーキテクチャ記事](https://tech.mirrativ.stream/entry/2023/09/22/112042)と、
> USN+VContainerの組み合わせを実装したOSSサンプル[mr-imada/OutgameSample](https://github.com/mr-imada/OutgameSample)
> (内部で[adarapata/ScreenSystem](https://github.com/adarapata/ScreenSystem)というUSN⇔VContainer橋渡しライブラリを使用)を
> 参考に、当初案(`IXxxView`インターフェース+`Atlas.Application`配置)から修正したもの。

### 各クラスの役割(修正: PresenterはPresentation層に置く)

当初`XxxPresenter`を`Atlas.Application`に置き、USNの`Page`型を参照しないための
`IXxxView`インターフェースを挟む設計にしていたが、これを撤回する。**Presenterは
Viewと同じ`Atlas.Presentation`に置き、`IXxxView`のような境界インターフェースは作らず、
具象の`XxxPage`を直接コンストラクタ注入で受け取る。**

理由: 参考にした2つの実装はどちらもPresenter相当(Mirrativ記事の「Lifecycle」、
OutgameSampleの`XxxLifecycle`)をPresentation層に置き、Viewの具象クラスに直接依存させている。
Mirrativ記事はこれを「Clean Architectureの厳密な分離よりシンプルさ・複雑さの低減を優先した」
判断と説明しており、Presenter⇔View間は同じ画面のために1対1で存在する密結合なペアなので、
インターフェースで抽象化しても差し替えの恩恵(テスト時のモック化以外)が薄い。一方、
`IXxxService`(Application)は複数画面から再利用され、かつ内部の`IXxxRepository`実体
(Mock/Real)を差し替える必要が実際にあるため、こちらだけインターフェース化する非対称な設計にする。

- **`XxxPage`(USNの`Page`継承、View、`Atlas.Presentation`に配置)**: ボタンの
  `OnClickAsObservable()`等をそのまま`Observable`プロパティとして公開し、`Refresh(dto)`では
  DTOの値をTextMeshPro/Image等へ反映するだけ。`IXxxService`や`Atlas.BattleCore`/
  `Domain.MasterData`の型を直接知らない
- **`XxxPresenter`(通常のC#クラス、`Atlas.Presentation`に配置)**: `XxxPage`(具象)と
  `IXxxService`(=Service)をコンストラクタ注入で受け取り、Viewの各Observableを
  `SubscribeAwait`等で購読する。購読処理の中でServiceを呼び、結果を`TViewDto`に詰めて
  `view.Refresh(dto)`を呼ぶ
- **Service = `IXxxService`**(既存の「コア進行ロジックのMock/Real切り替え」節で定義したもの)。
  新しい層を追加するのではなく、既存のRepository/Service抽象をそのままPresenterから呼ぶ

### クラス関係図

`PartyEditPage`(View)を例に、LifetimeScopeの階層とPresenter/View/Serviceの依存関係を
1枚にまとめる。上半分が「誰が誰を子として生成するか(DIスコープの所有関係)」、下半分が
「誰が誰をコンストラクタ注入で受け取るか(実行時の依存関係)」。

```
RootLifetimeScope(Bootstrapシーン、常駐)
  registers: IXxxConnection(Mock/Real選択済み), IXxxRepository, IPartyService, IMasterDataService...
  │
  │ LifetimeScope.EnqueueParent
  ▼
HomeLifetimeScope(Homeシーン)
  registers: PageContainer, ModalContainer, IScreenNavigator実装
  │
  │ LifetimeScope.EnqueueParent(IScreenNavigatorがPushのonLoad内で実行)
  ▼
PartyEditPageLifetimeScope : PageLifetimeScope<PartyEditViewDto>
  (PartyEditPage prefabに同梱、Instantiate直後・自動Build無効化・onLoad内で明示Build)
  Configure(builder):
    builder.RegisterComponent(view)           -- PartyEditPage(View)インスタンス
    RegisterViewDto(builder)                  -- Push時に渡されたPartyEditViewDto(null許容)
    builder.RegisterEntryPoint<PartyEditPresenter>()
  │
  │ Build()の副作用としてEntryPointDispatcherが自動構築(呼び出し側は明示的にResolveしない、
  │ 下記「DIによる結線とライフサイクル」参照)
  ▼
PartyEditPresenter ← コンストラクタ注入で依存 ─┬─ PartyEditPage(View、同じスコープでRegisterComponent済み)
  IInitializable/IDisposable実装               ├─ PartyEditViewDto(同じスコープでRegisterInstance済み)
  (Page破棄時にScopeごとDispose)                └─ IPartyService(親のRootLifetimeScopeまで遡って解決)

PartyEditPage(View)が公開するもの:
  Observable<Unit> OnSaveButtonClicked  -- PartyEditPresenter.Initialize()内でSubscribe
  void Refresh(PartyEditViewDto dto)    -- PartyEditPresenterから呼ばれる(初期表示・購読処理の両方から)
```

- 上半分(LifetimeScopeの親子)は「スコープの生存期間」を表す: Root=アプリ生存期間、
  Home=シーン生存期間、PartyEditPageLifetimeScope=そのPageの表示期間
- 下半分(コンストラクタ注入)は「実行時に誰が誰を握っているか」を表す: `IPartyService`
  だけがスコープを跨いで(Root→Page)解決される。`View`と`ViewDto`は同じPage単位スコープ内で
  完結する
- `PartyEditPage`自身は`PartyEditPresenter`を知らない(コンストラクタ注入の矢印はPresenter側
  からView側への一方向)。ViewはObservableとRefreshだけを公開し、誰が購読しているか・誰が
  Refreshを呼ぶかを意識しない

### DIによる結線とライフサイクル

OutgameSampleの[`PageBuilderBase.Build`](https://github.com/adarapata/ScreenSystem/blob/main/Assets/ScreenSystem/Runtime/Page/PageBuilderBase.cs)
で使われている実装をそのまま踏襲する。Page prefab自体に子`LifetimeScope`コンポーネントを
持たせておき(Viewと同じprefab内、自動Build無効化)、`onLoad`コールバックの中でそのスコープに
親を紐付けてから明示的に`Build`する。

**実装時に修正した点**: `IScreenNavigator`(の実装`ScreenNavigator`)は`TPage`(View)の型しか
知らず、画面ごとに異なる`XxxPresenter`の具体型を知らない。そのため`ScreenNavigator`側で
`lts.Container.Resolve<XxxPresenter>()`のように明示的に解決することはできない
(以前の版はここが誤りだった)。代わりに、各画面の`XxxPageLifetimeScope.Configure`内で
VContainerの`RegisterEntryPoint<XxxPresenter>()`を使う。これは`Build()`が呼ばれた時点で
自動的にPresenterを構築してくれる仕組みだが、**`XxxPresenter`が`IInitializable`等の
VContainerライフサイクルインターフェースを最低1つ実装していないと、`Dispatch()`が
解決対象に含めず一度も構築されない**(VContainer本体の`EntryPointDispatcher.Dispatch()`実装で
確認済み)。このため`XxxPresenter`は`IInitializable`を実装し、購読処理は
コンストラクタではなく`Initialize()`(`Dispatch()`から同期的に呼ばれる)に置く。

```
IScreenNavigator.PushPageAsync<XxxPage>()
  │
  ▼
using (LifetimeScope.EnqueueParent(sceneScope))   // 次にAwakeする子LifetimeScopeの親を予約
{
    PageContainer.Push<XxxPage>()(USN) -- Instantiate(prefab) --> XxxPage(GameObject)
      │ onLoad callback
      ▼
    var lts = page.gameObject.GetComponentInChildren<LifetimeScope>();  // Page prefab内の子スコープ
    lts.Build();          // ここでsceneScopeを親として構築される(EnqueueParentで予約済みのため)
    // XxxPresenterは呼び出し側(ScreenNavigator)が明示的にResolveするのではなく、
    // RegisterEntryPointの副作用としてBuild()内で自動的に構築される(下記参照)
}
```

```csharp
// Page prefab内の子LifetimeScope(自動Build無効化しておく)。ViewDtoを使わない画面でも
// PageLifetimeScope<TViewDto>は常に継承する(基底クラスを使い分けない、下記「ViewDto付きPush」参照)
public sealed class XxxPageLifetimeScope : PageLifetimeScope<XxxViewDto>
{
    [SerializeField] private XxxPage view;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterComponent(view);
        RegisterViewDto(builder);
        // RegisterEntryPointは「Build()時に自動構築してほしい」という意図を表す。
        // XxxPresenterがIInitializableを実装していない場合、これだけでは一度も
        // 構築されない点に注意(下記XxxPresenter参照)
        builder.RegisterEntryPoint<XxxPresenter>();
        // IXxxServiceは親(シーン/Root)スコープに登録済みなので、ここでは何もしなくても解決できる
    }
}

public sealed class XxxPresenter : IInitializable, IDisposable
{
    private readonly XxxPage view;
    private readonly IXxxService service;
    private readonly CompositeDisposable disposables = new();

    [Inject]
    public XxxPresenter(XxxPage view, IXxxService service)
    {
        this.view = view;
        this.service = service;
    }

    // Build()内でRegisterEntryPointの副作用として同期的に呼ばれる。
    // ここで初めてViewのObservable購読を開始する(コンストラクタでは行わない)
    public void Initialize()
    {
        view.OnXxxClicked
            .SubscribeAwait(async (_, ct) =>
            {
                var result = await service.DoSomethingAsync();        // Service呼び出し(CancellationTokenは渡さない、下記参照)
                view.Refresh(new XxxViewDto(...));                    // Pageのリフレッシュ
            })
            .AddTo(disposables);
    }

    public void Dispose() => disposables.Dispose();
}
```

- Page破棄(Pop)時、Page prefab内の子`LifetimeScope`も一緒に破棄される。VContainerは解決済み
  インスタンスが`IDisposable`なら破棄時に`Dispose`を呼ぶため、`XxxPresenter`が`IDisposable`を
  実装して`disposables`(購読の集合)を`Dispose`すれば、画面遷移のたびに購読が自動的に解除される
  (明示的な`OnPagePop`フック等は不要)
- `LifetimeScope.EnqueueParent(parent)`は、[PageBuilderBase.cs](https://github.com/adarapata/ScreenSystem/blob/main/Assets/ScreenSystem/Runtime/Page/PageBuilderBase.cs)
  で実際に使われているのを確認済みのAPI。以前「未検証」としていたVContainerのマルチシーン
  (動的Instantiateされたprefabへの親スコープ紐付け)の仕組みはこれで確定する
- 上記の`RegisterEntryPoint`+`IInitializable`の組み合わせは、実際に`Atlas.Presentation`
  (`Client/AtlasUnityProject/Assets/Scripts/Presentation/`)の`ScreenNavigator`実装時に
  コンパイル・VContainer本体のソースで動作を確認済み
- **`IInitializable`と`IAsyncStartable`の使い分け**: Presenterの初期化が同期で完結する場合は
  `IInitializable`(`void Initialize()`)、`ISignInService.SignInAsync`のように非同期の
  初期データ取得が必要な場合は`IAsyncStartable`(`UniTask StartAsync(CancellationToken)`)を
  使う。`RegisterEntryPoint`はどちらも解決対象にする(`EntryPointDispatcher.Dispatch()`の
  `IAsyncStartable`一覧解決を確認済み)ため、Presenterごとに必要な方を選べばよい。
  `HomePresenter`(`Presentation/Home/`)が`IAsyncStartable`の実例

**検討した代替案(不採用): `RegisterFactory`+`AddTo(GameObject)`**。USN公式デモ
(`Demo/Core/Scripts/Composition/`)はDIコンテナを一切使わず、`XxxPresenterFactory`を
手書きし、Presenterの破棄も`AddTo(page.gameObject)`(UniRx)で行っている。これをVContainerの
`RegisterFactory`(DI解決する依存+実行時引数を両方受け取れるFactory登録)で再現すれば、
Page prefabへのLifetimeScope埋め込みと`EnqueueParent`を丸ごと廃止できる案として検討したが、
**不採用とした**。理由: Presenterの生存期間管理がVContainerのスコープ機構(現行案)とR3の
`AddTo`(代替案)とでライブラリをまたいで分裂するのを避け、DIコンテナに一貫して寄せる方を
優先したため。

### 未決定: DTOの粒度(パフォーマンス方針)

`Refresh(TViewDto dto)`を「画面全体のDTOを毎回作り直す(アウトゲーム向け、更新漏れが
起きにくいがDTO再生成が無駄)」にするか「差分だけを渡す複数の`RefreshXxx`メソッドに
分ける(バトル向け、頻繁な更新に対して無駄が少ないが更新漏れのリスクがある)」かは、
**画面ごとに実装時に判断する**。本ドキュメントでは全画面共通のルールとしては決めない
(アウトゲームとバトルで要求されるパフォーマンス特性が異なるため、一律の方針を今
決め打ちするのは時期尚早と判断)。

### 画面をまたぐ通知(IMessageBroker)

同じ画面内のView→Presenter通知は上記の通り直接Observable購読で完結させる。
画面をまたぐ通知には、Supplementの`Supplement.Core.IMessageBroker`
(実装は`Supplement.ZeroMessenger.GlobalMessageBroker`、[ZeroMessenger](https://github.com/AnnulusGames/ZeroMessenger)
のラッパー)を使う。MessagePipeの導入も検討したが、Supplementに既に同種の仕組みがあり
二重導入になるため不採用にした。

```csharp
namespace Supplement.Core
{
    public interface IMessageBroker
    {
        void Publish<T>(T message) where T : struct;
        IDisposable Subscribe<T>(Action<T> handler) where T : struct;
    }
}
```

- 使うのは**「1つの画面だけでは完結しない事柄」に限定**する
  - 該当する例: トースト通知(どの画面からでも出せる必要がある)、gems残高の変更通知
    (Home内のどの画面からでも最新値を表示したい)等、**送信元・受信先が特定の画面ペアに
    限定されない**もの
  - 該当しない例: あるボタンを押したら同じ画面のPresenterが処理する、というような
    1画面内で完結する通知(直接Observable購読を使う)。Pushした画面から結果を受け取る
    ケースも該当しない(「Pop結果の受け渡し」の`WaitForPopAsync`で既に解決済み)
- **スコープ管理を意識する必要がない**: `GlobalMessageBroker`は内部で
  `MessageBroker<T>.Default`(ZeroMessengerの型ごとの静的インスタンス)に委譲するだけの
  ステートレスな実装なので、VContainerのどのスコープで`IMessageBroker`を解決しても
  同じチャンネルに繋がる。MessagePipeのように「どのLifetimeScopeに登録するか」で
  チャンネルが分かれる、という考慮が不要(MessagePipe採用案を不採用にした副次的な理由でもある)
- 登録は`RootLifetimeScope.Configure`内で`builder.Register<IMessageBroker, GlobalMessageBroker>(Lifetime.Singleton)`
  を1回行うだけでよい(実体はステートレスなので、実際にはどのスコープで登録しても動作は
  変わらないが、他の横断的コンポーネントと合わせてRootに置く)
- **メッセージ型は`struct`制約**(`IMessageBroker`のシグネチャ参照)。`class`にはできない
- メッセージ型は`Xxx`(画面名)ではなく、通知内容そのものの名前にする(例:
  `GemsBalanceChangedMessage`、`ToastRequestedMessage`)。画面名を冠さないことで
  「特定の画面に紐づかない」という性質を型名からも読み取れるようにする

## VContainerスコープ設計

シーン構成(Bootstrap/Home/Battle)に合わせて3階層にする。

- **RootLifetimeScope**(Bootstrapシーン、アプリ生存期間中ずっと存在):
  - `MasterDataLoader`が読み込んだ`MemoryDatabase`
  - 各`IXxxConnection`実装(Mock/Realの選択はここで行う、詳細は下記)、各`IXxxRepository`実装、
    各`IXxxService`実装
- **シーンLifetimeScope**(`HomeLifetimeScope`/`BattleLifetimeScope`、対応するシーンが
  ロードされている間だけ存在): `RootLifetimeScope`の子として生成する。そのシーン固有の
  USN`PageContainer`/`ModalContainer`をここで登録する。呼び出し側(Presenter等)に
  `PageContainer`を直接触らせず、`IScreenNavigator`のような薄いラッパー越しに`Push`/`Pop`
  させることを推奨する(Presenterのテスト時にUSNの型をモックせずに済む)
- **Page単位の子スコープ**: Page prefab自体に子`LifetimeScope`(自動Build無効化)を持たせておき、
  `IScreenNavigator`が`Push`の`onLoad`コールバック内で`LifetimeScope.EnqueueParent(sceneScope)`
  →`lts.Build()`という順で構築する。Presenterは`ScreenNavigator`が明示的にResolveするのではなく、
  各画面の`Configure`内の`RegisterEntryPoint<XxxPresenter>()`の副作用として`Build()`内で
  自動構築される(詳細は下記「責務分担(Page / View / Presenter / Service)とデータフロー」参照)
- Page破棄(Pop)時に子スコープも一緒に破棄され、Presenterのライフタイムは画面の表示期間と一致する
- Home/BattleシーンのLifetimeScopeを別シーンにある`RootLifetimeScope`の子にする部分も、同じ
  `LifetimeScope.EnqueueParent(parent)`で行える見込み(Page単位の子スコープと同じAPI)。
  Bootstrap→Home疎通実装時に実際に動作するか確認する

## IScreenNavigator

USNの`PageContainer`/`ModalContainer`をラップし、呼び出し側(Presenter等)にUSNの型を
直接触らせないための抽象。実クラス([PageContainer.cs](https://github.com/Haruma-K/UnityScreenNavigator/blob/main/Assets/UnityScreenNavigator/Runtime/Core/Page/PageContainer.cs))
に基づいて設計する。

> **画面遷移エンジン自体はUSNのまま**にする。自前実装の[ScreenService](https://github.com/adarapata/ScreenSystem)
> (`ViewCachePool`/`LayerObjectContainer`/`SortingLayerController`等でView管理を完全に
> 自作している)は不採用。理由は、ScreenServiceのView管理部分をAtlasへ持ち込んでも
> USNの相当機能を置き換えるだけで得るものが無い一方、ScreenServiceの価値の中心である
> オーケストレーション部分(トランザクションキュー等)は今回不要と判断したため。
> **ScreenServiceから実際に採用するのは「Push時にDTOを渡す」「Pop時に結果を返す」という
> API上のエルゴノミクスのみ**で、これは以下のようにUSNの上に薄く実装できる。

```csharp
public interface IScreenNavigator
{
    UniTask<TPage> PushPageAsync<TPage>(bool playAnimation = true, bool stack = true, string resourceKey = null)
        where TPage : Page;
    UniTask<TPage> PushPageAsync<TPage, TViewDto>(TViewDto dto, bool playAnimation = true,
        bool stack = true, string resourceKey = null) where TPage : Page;

    UniTask PopPageAsync(bool playAnimation = true, int popCount = 1);
    UniTask PopPageAsync<TResult>(TResult result, bool playAnimation = true);
    UniTask<TResult> WaitForPopAsync<TResult>(Page target, CancellationToken token);

    UniTask<TModal> PushModalAsync<TModal>(bool playAnimation = true, string resourceKey = null)
        where TModal : Modal;
    UniTask<TModal> PushModalAsync<TModal, TViewDto>(TViewDto dto, bool playAnimation = true,
        string resourceKey = null) where TModal : Modal;

    UniTask PopModalAsync(bool playAnimation = true, int popCount = 1);
    UniTask PopModalAsync<TResult>(TResult result, bool playAnimation = true);
    UniTask<TResult> WaitForPopModalAsync<TResult>(Modal target, CancellationToken token);
}
```

### ViewDto付きPush(Push時の入力と初期表示データを1つの型に統一)

当初「Push時の入力(`Parameter`)」と「Refresh時の表示データ(`ViewDto`)」を別の型として
分けていたが、これを撤回して**同じ`XxxViewDto`に統一する**。ScreenServiceも
`PushAsync(ViewDto dto)`で受け取った同じ`ViewDto`をそのまま`View.RenderAsync(viewDto)`の
初期表示に使っており、Push時点で分かっている情報と初期表示に必要な情報は実質同じもの
だったため、型を分ける意味が薄いと判断した。

[adarapata/ScreenSystem](https://github.com/adarapata/ScreenSystem)の
`PageBuilderBase<TPage, TPageView, TParameter>`/`LifetimeScopeWithParameter<T>`(実例で
動作確認済み)と同じ仕組みを、型引数の意味だけ`TViewDto`に読み替えてそのまま使う。
Page prefab同梱の子`LifetimeScope`が`PageLifetimeScope<TViewDto>`を継承していれば、
`Build()`前に`SetViewDto`で値を渡せるようにする。

```csharp
public abstract class PageLifetimeScope<TViewDto> : LifetimeScope
    where TViewDto : class
{
    protected TViewDto ViewDto { get; private set; }
    public void SetViewDto(TViewDto dto) => ViewDto = dto;

    // VContainerのRegisterInstanceはnullを渡すとNullReferenceExceptionになるため、
    // ViewDtoを使わない画面(SetViewDtoが一度も呼ばれずnullのまま)向けにガードする。
    // Configure内ではbuilder.RegisterInstance(ViewDto)ではなくこちらを使うこと
    // (実装時にVContainer本体のソースで挙動を確認済み)
    protected void RegisterViewDto(IContainerBuilder builder)
    {
        if (ViewDto != null)
        {
            builder.RegisterInstance(ViewDto);
        }
    }
}

// 画面ごとの子LifetimeScope(Push時に初期データを渡す画面はこちらを継承する)
public sealed class PartyEditPageLifetimeScope : PageLifetimeScope<PartyEditViewDto>
{
    [SerializeField] private PartyEditPage view;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterComponent(view);
        RegisterViewDto(builder);
        builder.RegisterEntryPoint<PartyEditPresenter>();  // Build()時に自動構築させる(下記「DIによる結線とライフサイクル」参照)
    }
}

public sealed class PartyEditPresenter : IInitializable, IDisposable
{
    private readonly PartyEditPage view;
    private readonly IPartyService partyService;
    private readonly PartyEditViewDto initialDto;

    [Inject]
    public PartyEditPresenter(PartyEditPage view, IPartyService partyService, PartyEditViewDto initialDto)
    {
        this.view = view;
        this.partyService = partyService;
        this.initialDto = initialDto;
    }

    public void Initialize()
    {
        view.Refresh(initialDto);   // Push元が渡した値でそのまま初期表示する(追加の変換不要)
        // ここから先、購読処理の中でServiceを呼んだ結果を新しいPartyEditViewDtoに詰めて
        // 再度view.Refresh(dto)する流れは他の画面と同じ
    }

    public void Dispose() { /* 購読があればここでDispose */ }
}
```

`IScreenNavigator`側の実装は、`onLoad`コールバック内で`lts.Build()`を呼ぶ前に、
`SetViewDto(dto)`を呼ぶ。**Push時に渡すデータが無い画面でも`PageLifetimeScope<TViewDto>`は
常に継承する**(基底クラスを使い分けない)。`SetViewDto`が一度も呼ばれなければ`ViewDto`は
`null`のままになるだけで、`Configure`内は`RegisterViewDto(builder)`を呼んでおけば
(`ViewDto`が`null`なら何も登録しない)安全。`PushPageAsync<TPage>()`(引数無しオーバーロード)は
この`null`のケースに対応する呼び出し方で、`TViewDto`という制約上`class`型のみ対応する
(`where TViewDto : class`)。

### Pop結果の受け渡し

Push元が「Pushした画面が閉じたときの結果」を型付きで受け取れるようにする。ScreenServiceの
`IPresenter.CompleteAsync()`+`WaitForPopAsync<T>`と同じ考え方を、USNのPage単位で実装する。

```csharp
// Push元(呼び出し側)
var partyEditPage = await screenNavigator.PushPageAsync<PartyEditPage, PartyEditViewDto>(dto);
var updatedParty = await screenNavigator.WaitForPopAsync<PartyEditResult>(partyEditPage, ct);

// PartyEditPresenter側(保存ボタン押下時)
await screenNavigator.PopPageAsync(new PartyEditResult(...));
```

- `IScreenNavigator`の実装は、Push済みの`Page`インスタンスをキーにした
  `Dictionary<Page, UniTaskCompletionSource<object>>`を内部に持つ
- `WaitForPopAsync<TResult>(target, token)`は該当`target`の`UniTaskCompletionSource`を
  (無ければ生成して)待つ。同じ`target`に対して二重に呼ばれた場合は例外にする
  (ScreenServiceの`WaitForPop`と同じ制約)
- `PopPageAsync<TResult>(result, ...)`は、実際に`PageContainer.Pop`する前に該当する
  `UniTaskCompletionSource`があれば`result`で完了させる
- 結果を渡さない通常の`PopPageAsync()`(戻る・キャンセル等)でも、保留中の
  `UniTaskCompletionSource`があれば`default(TResult)`で完了させ、`WaitForPopAsync`側が
  ハングしないようにする
- Modal側(`WaitForPopModalAsync`)も同じ仕組みを`Modal`インスタンスキーで持つ

### データ受け渡し型の命名規則

画面をまたいで受け渡すデータは2種類。**すべて画面名(`Xxx`)をプレフィックスにし、
サフィックスで役割を区別する**。

| サフィックス | 役割 | 受け渡し方向 | 例(`PartyEdit`画面) |
|---|---|---|---|
| `XxxViewDto` | Push時の入力、およびPresenterがViewを再描画するための表示データ(`Refresh(dto)`)。両者は同じ型 | Push元 → 新しい画面 / Presenter → 同じ画面のView | `PartyEditViewDto` |
| `XxxResult` | Pop時にPush元へ返す結果 | 閉じる画面 → Push元 | `PartyEditResult` |

例えば`PartyEditPage`関連の型は`PartyEditViewDto`/`PartyEditResult`/`PartyEditPresenter`/
`PartyEditPageLifetimeScope`と、すべて画面名で揃える。

- `resourceKey`は省略可能にし、省略時は`typeof(TPage).Name`(Modalも同様)をそのまま
  Addressableのアドレスとして使う。Page/Modalの各prefabは、**Addressableアドレスを
  対応するC#クラス名と完全一致させる**という命名規約に統一する
  (`Assets/Addressables/Views/{機能名}/{PageClassName}.prefab`、アドレスも`{PageClassName}`)。
  型ごとの定数クラスやenumによるマッピング表は用意しない(画面が増えるたびにメンテナンスが
  必要になり、実体(prefab)との二重管理になるため)。呼び出し側は基本`PushPageAsync<TitlePage>()`
  のように型引数だけで完結する
  - 命名規約から外れるとアドレス解決に失敗し、Push時に`AssetLoadStatus.Failed`として
    即座に例外で検知できる(実行時に気づける、サイレントに壊れない)ため、命名規約を破った
    場合の検知コストは低いと判断する
  - `resourceKey`の明示指定は、将来ローカライズ差し替え等で1つの型に複数のprefabを
    対応させたくなった場合の抜け道として残す(現状は使わない想定)
  - **USN公式デモ(`Demo/Core/Scripts/Foundation/Common/ResourceKey.cs`)との乖離について**:
    公式デモは`ResourceKey.Prefabs.TopPage`のような一元管理された定数クラスを使っており、
    Atlasのクラス名規約とは異なる。これは意図的な選択で不採用にしたわけではなく、
    上記の「型ごとの定数クラス〜二重管理になるため」という理由を優先した結果の乖離である。
    定数クラス方式の利点(コンパイル時に存在チェックできる)よりも、実体(prefab)との
    二重管理を避ける利点を優先したという判断であり、変更の必要は無いと判断する

- `PageContainer.Push<TPage>`/`ModalContainer.Push<TModal>`は`AsyncProcessHandle`
  (コルーチンベース)を返す。`AsyncProcessHandle.Task`(`System.Threading.Tasks.Task<object>`)を
  `.AsUniTask()`することでUniTaskに変換し、Atlas全体のUniTask前提の設計と揃える
- `stack: false`にするとPushと同時に直前のPageを破棄する(タイトル→ホームのように戻る必要が
  ない遷移で使う)。`stack: true`(デフォルト)は履歴に積む(戻るボタン付きの遷移で使う)
- DI注入のフック地点は`Push`の`onLoad`コールバック。Page(GameObject)がInstantiateされた
  直後、そのPage自身のライフサイクル(`AfterLoad`等)が走るより前に呼ばれる。ここで、
  Page prefab内に同梱した子`LifetimeScope`を`LifetimeScope.EnqueueParent(sceneScope)`で
  親付けしてから`Build()`する(`IObjectResolver.Inject`は使わない)。Presenterは`ScreenNavigator`が
  明示的にResolveするのではなく`RegisterEntryPoint`の副作用として構築される
  (詳細は「責務分担(Page / View / Presenter / Service)とデータフロー」参照)。
  この一連の処理は画面ごとに書かず、`IScreenNavigator`の実装1箇所にまとめる
- `IObjectResolver.Instantiate`(Instantiate+Inject一体型)への統一は**不可能と確認済み**。
  `IAssetLoader.LoadAsync<GameObject>`が返すのはPrefabアセットであり、実際の
  `Instantiate`呼び出しは`PageContainer`内でUnity標準の`Object.Instantiate`が直接
  ハードコードされている(差し替え用のフック・委譲先が無い)。USN側を改造しない前提のため、
  「先にUSNがInstantiate→`onLoad`で後から子スコープをBuild」以外の経路は取れない

## コア進行ロジックのMock/Real切り替え(Connection / Repository / Service)

バトルは[battle.md](battle.md)で`IBattleConnection`(`MockBattleConnection`/
`RealtimeBattleConnection`)として既に定義済み。この「Presenterは抽象インターフェースしか
知らず、実装がMock(プロセス内完結)かReal(実際の通信)かをDI側で切り替える」というパターンを
アウトゲームにも適用する。アウトゲームでは「サーバーとの通信」と「クライアントが保持する
所持データ」を別の抽象に分け、以下の3種類で構成する(下記「サインインでの実装例」参照)。

- **`Atlas.Application`: `IXxxConnection`** — サーバーAPI呼び出しのポート
  (`IDeviceConnection`/`IPlayerConnection`)。Mock/Realの切り替えはこの単位で行う。
  Entityの永続化ではなくAPIアクション(登録・認証・サインイン等)の呼び出しなので
  「Repository」とは呼ばない
- **`Atlas.Domain`: `IXxxRepository`** — クライアントがローカルに保持する所持データ(Entity)の
  読み書き。[architecture.md](architecture.md)「APIレスポンス設計」の`playerDiff`の
  **リソース種別ごとに1つ**用意する(`items`→`IItemRepository`、`pachimon`→`IPachimonRepository`、
  `pachimonMoveMap`→`IPachimonMoveMapRepository`、`partySlots`→`IPartyRepository`)。通信方式は一切知らず、Mock/Real
  切り替えの対象でもない
- **`Atlas.Application`: `IXxxService`** — Presenterが直接コンストラクタ注入で依存する抽象
  (`ISignInService`/`IPlayerAccountService`)。「Connectionを呼ぶ→結果をRepositoryへ反映する」
  という一連の流れや、Repositoryからの読み出しをまとめる。実装(`XxxService`)は
  `Atlas.Infrastructure`(base)に1つだけ置き、Mock/Realの違いを意識しない

**`IXxxConnection`/`IXxxRepository`/`IXxxService`のメソッドは`CancellationToken`を引数に取らない**
(`IPlayerConnection.SignInAsync()`/`ISignInService.SignInAsync()`参照)。理由:

- これらはRootシーンで`Lifetime.Singleton`登録されるため、そもそも特定のPageの表示期間には
  紐付かない。呼び出し元のPresenter(Page単位スコープ)が`IAsyncStartable.StartAsync`等で
  受け取る`CancellationToken`をそのまま素通しさせても、Singletonであるサービス側の処理を
  Page単位で正しく打ち切れる保証はない(他のPageからの呼び出しと共有され得るため)
- Presenter側で個別に途中キャンセルしたい場合は、呼び出し側で`UniTask.WithCancellation`等を
  使ってPresenter側の責務として実装する。インターフェース自体にはキャンセルの概念を持ち込まない
- Real実装がHTTPリクエストを中断したい場合は、実装内部で
  `UnityEngine.Application.exitCancellationToken`(アプリ終了時にキャンセル)等、実装側で
  完結する手段を使う。インターフェースの契約には影響しない
- バトルの`IBattleConnection`のように、呼び出し単位でもPage単位でもない別のライフサイクル
  (セッション単位)でのキャンセルが本当に必要な場合は、このルールの例外として個別に検討する

### `playerDiff`の適用

`playerDiff`を返すAPI(サインイン・スカウト・パーティ編成・技の付け替え)の
`XxxConnection`実装は、レスポンス受信直後に`IPlayerDiffApplier.ApplyAsync(response.PlayerDiff)`を
呼ぶ。`PlayerDiffApplier`はリソース種別ごとの`XxxDiffApplier`(`ItemDiffApplier`等)へ
振り分けるだけの窓口で、各`XxxDiffApplier`が`upserted`/`removed`を対応する
`IXxxRepository`の`Upsert`/`Delete`へ変換する。これにより、APIごとに反映処理を書かずに
済む([architecture.md](architecture.md)「クライアント側の反映」の方針)。

- `IPlayerDiffApplier`/`XxxDiffApplier`は生成DTO(`PlayerDiffDto`/`ItemsDiffDto`等)を
  直接受け取るため、`Atlas.Infrastructure.Api`に置く。Service側(`SignInService`等)は
  `playerDiff`の存在を知らず、Connectionを呼ぶだけで各Repositoryが最新化された状態になる
- 複数リソースをまとめた汎用のDiff型・汎用Repositoryは作らない。リソース種別ごとに
  専用の`XxxDiffApplier`+`IXxxRepository`を1組ずつ追加する
- `playerId`/`nickname`は`playerDiff`に含まれない(architecture.md参照)ため、
  `IPlayerConnection.SignInAsync`の戻り値(`SignInResult`)として返し、`SignInService`が
  `IPlayerProfileRepository`へ保存する
- 現状のRepository実装(`ApiItemRepository`/`ApiPachimonRepository`/`ApiPachimonMoveMapRepository`/
  `ApiPartyRepository`/`ApiPlayerProfileRepository`)はメモリ保持のみで、
  ローカルディスクへは永続化しない。サーバーのDBが正であり、起動時に毎回`POST /sign-in`で
  フルスナップショットを取り直せるため

### サインインでの実装例

```
BootstrapEntryPoint
  └─ ISignInService.SignInAsync()                          (Application / 実装: Infrastructure)
       ├─ IDeviceCredentialsRepository                       device_id/secret_keyの暗号化ローカル保存
       ├─ IDeviceConnection.RegisterAsync                    (Application / 実装: Infrastructure.Api)
       ├─ AccessTokenRefresher.RefreshAsync                  (Infrastructure.Api)
       │    └─ IDeviceConnection.AuthenticateAsync → AccessTokenStore.SetToken
       ├─ IPlayerConnection.SignInAsync ─┬─ POST /sign-in(404ならSignUpAsync→再度SignInAsync)
       │                                 └─ IPlayerDiffApplier.ApplyAsync(playerDiff)
       │                                      ├─ ItemDiffApplier            → IItemRepository.Upsert/Delete
       │                                      ├─ PachimonDiffApplier        → IPachimonRepository.Upsert/Delete
       │                                      ├─ PachimonMoveMapDiffApplier → IPachimonMoveMapRepository.Upsert/Delete
       │                                      └─ PartyDiffApplier           → IPartyRepository.Upsert/Delete
       └─ IPlayerProfileRepository.SaveAsync(PlayerProfile)

HomePresenter
  └─ IPlayerAccountService.Get() → IPlayerProfileRepository.Get()
```

### アクセストークンの更新

[outgame.md](outgame.md)「デバイス認証」補足の方針(リフレッシュトークンは無く、
`POST /devices/authenticate`の再実行で更新する)を、`Atlas.Infrastructure.Api`の
`AccessTokenRefresher`で実装する。トークンを書き込むのは`AccessTokenRefresher`だけにする。

- 要認証APIを叩く`XxxConnection`は、生成`XxxApiClient`の呼び出しを
  `AccessTokenRefresher.SendAsync(() => client.XxxAsync(...))`で包む
- **送信前チェック(通信なし)**: `AccessTokenStore`が持つ有効期限(受信時の端末時刻 + `expiresIn`)の
  残りが5分未満なら、送信前に再認証する。バックグラウンドから戻った直後もここで更新される
- **401リトライ**: それでも401が返った場合は、再認証してから1回だけリトライする
- **再認証は同時に1つだけ**: サーバーは再認証時に古いトークンをすぐ無効にするため、実行中の
  再認証があればそれを共有して待つ。401を受けたときに、送信時のトークンが既に差し替わっていれば
  再認証せずにリトライする

検討したが採用しなかった案:

- **一定間隔のタイマーで期限をチェックする**: 送信前チェックで同じことができ、しかも
  バックグラウンド復帰直後の失効も防げるため不要
- **毎回のリクエスト前に`/devices/authenticate`や`/auth/verify`を呼ぶ**: 認証はArgon2で照合するため
  サーバー負荷が大きい。さらに、再認証のたびに並行リクエストのトークンを無効にしてしまう。
  `/auth/verify`で得られる情報は端末内の期限チェックとほぼ同じで、検証から送信までの間の
  失効も防げない

### インターフェース単位

`IXxxConnection`は、`api-codegen`が生成する`XxxApiClient`単位(Device/Player/Chat/Scout)に
それぞれ対応させる(1つの巨大な`IOutgameConnection`にはしない)。理由: 「スカウトだけ本番
サーバーに繋いで他はMockのまま動作確認する」のような部分的な切り替えがしやすいため。

- 実装済み: `IDeviceConnection`(`POST /devices`・`POST /devices/authenticate`)、
  `IPlayerConnection`(`POST /signup`・`POST /sign-in`・`POST /edit/party`)、
  `IScoutConnection`(`GET /scout/banners`・`POST /scout/rolls`・`POST /scout/rolls/{rollId}/select`)
- 未実装: Chat、および技の付け替え(`POST /edit/pachimon_moves`)用のConnection。
  いずれも対応する画面の実装時に追加する
- `IXxxRepository`は上記の通り`playerDiff`のリソース種別単位であり、Connectionの単位とは
  一致しない(1つのConnectionが複数リソースの差分を返し得るため)
- 対戦は、マッチング(REST)とバトル本体(MagicOnion)でライフサイクルが違うため2つに分ける。
  `IBattleMatchmaker`(`Atlas.Application`、`POST /battle/queue`+`GET /battle/queue/status`の
  ポーリングをまとめてマッチ成立(`BattleMatch`)まで待つ)はRoot常駐のSingleton。
  `IBattleConnection`はセッション単位のため直接登録せず、`IBattleConnectionFactory`
  (`Atlas.Application`)をSingleton登録してBattleシーンのスコープが対戦ごとに生成する

各インターフェースの具体的なメソッド・Payload形状は、対応する画面を実装するタイミングで
`design/outgame.md`のAPI仕様に合わせて個別に定義する(本ドキュメントではパターンのみ規定し、
先回りして全メソッドを定義することはしない)。

### 実装の配置

- `Atlas.Infrastructure`(base): `XxxService : IXxxService`(`SignInService`/
  `PlayerAccountService`)と、ローカルファイルへの永続化を伴うRepository
  (`DeviceCredentialsRepository`、Supplementの`IFileStorageService`経由)
- `Atlas.Infrastructure.Api`: `api-codegen`生成物(`Generated/`配下、再生成で上書きされる)に加え、
  それを呼ぶReal実装(`DeviceConnection`/`PlayerConnection`/`ScoutConnection`/`ApiBattleMatchmaker`)、`PlayerDiffApplier`/
  `XxxDiffApplier`、メモリ保持のRepository実装(`ApiItemRepository`/`ApiPachimonRepository`/
  `ApiPachimonMoveMapRepository`/`ApiPartyRepository`/`ApiPlayerProfileRepository`)、`AccessTokenStore`。手書きのコードは`Generated/`の外に置き、
  生成DTOを`Atlas.Domain`/`Atlas.Application`側の型へ詰め替える
- `Atlas.Infrastructure.Mock`: `MockXxxConnection`(`MockDeviceConnection`/
  `MockPlayerConnection`)と`MockBattleConnection`/`MockBattleConnectionFactory`/
  `MockBattleMatchmaker`。アウトゲームのMockは固定値を返すだけ。`IScoutConnection`のMockは
  Mockへ差し替える場面(PlayModeテスト等)が無いため作っていない
- `Atlas.Infrastructure.Realtime`: `RealtimeBattleConnection`/`RealtimeBattleConnectionFactory`
  (MagicOnion+YetAnotherHttpHandler)。BattleServerとの通信契約(`IBattleHub`/Payload)は
  `Shared/BattleContracts/`(ローカルパッケージ`Atlas.BattleContracts`)をBattleServerと共有する

### 切り替え方法

- `RootLifetimeScope`の`protected virtual void ConfigureAuthConnections(IContainerBuilder)`に
  `IDeviceConnection`/`IPlayerConnection`の登録だけを切り出し、本番は常にReal実装を登録する。
  Mockへの差し替えはこれをoverrideした派生スコープで行う(Battle PlayModeテストの
  `TestRootLifetimeScope`が、ローカルAPIサーバー無しでBootstrap→Home→Battleの起動経路を
  検証するために使用)。`IXxxRepository`/`IXxxService`の登録はMock/Realに関わらず常に同じ
- `IScoutConnection`は差し替え用のvirtualメソッドに切り出さず、`Configure`で常にReal実装を登録する
- 対戦(`IBattleMatchmaker`/`IBattleConnectionFactory`)は`ConfigureBattleConnections`(virtual)に
  切り出し、Bootstrapシーンの`RootLifetimeScope`のInspector(`Use Battle Server`)で切り替える。
  既定はオン(実サーバー。オフでMock、サーバー不要)。`TestRootLifetimeScope`はこの設定に関わらず常にMockにする
- Connectionが増えて「機能ごとにInspectorからMock/Realをトグルしたい」要求が出た時点で、
  機能ごとの`RepositoryMode { Mock, Real }`を持つScriptableObject(`RepositoryConfig`)を
  導入する案を再検討する(現状は未導入)

**検討したが採用しなかった案: `IXxxRepository`をMock/Real切り替えの単位にする構成**。
当初は`IPlayerRepository`(Domain、`GetMeAsync()`でサーバーから都度取得)に
`MockPlayerRepository`/`RealPlayerRepository`を用意し、`IPlayerService`がそれを
パススルーする形だった。`playerDiff`共通レスポンス形式の導入(サーバーが返すのが
「取得結果」ではなく「クライアント保持データへの差分」になった)に伴い、Repositoryを
「ローカル保持データ」、通信を「Connection」に分離する現行構成へ置き換えた。

## asmdef構成との対応

| asmdef | 配置する型 | 参照 |
|---|---|---|
| `Atlas.Domain` | Entity(`PlayerProfile`/`ItemEntity`/`DeviceCredentials`等)、`IXxxRepository`群 | UniTaskのみ |
| `Atlas.Application` | `IXxxService`群(Presenterが直接依存する抽象)、`IXxxConnection`群(通信ポート)、`IBattleMatchmaker`/`IBattleConnectionFactory`、それらの戻り値型(`PlayerData`/`SignInResult`/`AuthenticationResult`/`BattleMatch`/`ScoutBanner`/`ScoutRoll`/`ScoutCandidate`) | `Atlas.Domain`, `Atlas.MasterData`, UniTask |
| `Atlas.Infrastructure`(base) | `XxxService`(`IXxxService`の実装)、`DeviceCredentialsRepository`、`MasterDataService` | `Atlas.Domain`, `Atlas.Application`, `Atlas.Infrastructure.Api`, `Atlas.MasterData`, UniTask, Supplement等 |
| `Atlas.Infrastructure.Api` | `api-codegen`生成物(`Generated/`)、`XxxConnection`(Real実装)、`PlayerDiffApplier`/`XxxDiffApplier`、`ApiXxxRepository`、`AccessTokenStore` | `Atlas.Domain`, `Atlas.Application`, UniTask |
| `Atlas.Infrastructure.Mock` | `MockXxxConnection`、`MockBattleConnection`/`MockBattleConnectionFactory`/`MockBattleMatchmaker` | `Atlas.Domain`, `Atlas.Application`, `Atlas.MasterData`, `Atlas.BattleCore`, UniTask |
| `Atlas.Infrastructure.Realtime` | `RealtimeBattleConnection`/`RealtimeBattleConnectionFactory`(MagicOnionクライアント) | `Atlas.Domain`, `Atlas.Application`, `Atlas.BattleContracts`, `Atlas.BattleCore`, MagicOnion.Client, YetAnotherHttpHandler, UniTask |
| `Atlas.BattleContracts`(`Shared/BattleContracts/`、ローカルパッケージ) | `IBattleHub`/`IBattleHubReceiver`とPayload型。BattleServerと共有する通信契約 | `Atlas.BattleCore`, MessagePack, MagicOnion.Abstractions |
| `Atlas.Navigation` | `IScreenNavigator`/`ScreenNavigator`(USNの`PageContainer`/`ModalContainer`をラップする画面遷移基盤)、`PageLifetimeScope<TViewDto>`基底クラス | USN, VContainer, UniTaskのみ |
| `Atlas.Presentation` | USNの`Page`派生クラス(View)、`XxxPresenter`、`XxxViewDto`、Page同梱の子`LifetimeScope`(`XxxPageLifetimeScope`)。**画面単位**の型のみを持ち、シーン単位のスコープは持たない | `Atlas.Domain`, `Atlas.Application`, `Atlas.Navigation`, USN, VContainer, R3 |
| `Atlas.DI` | `RootLifetimeScope`/`HomeLifetimeScope`/`BattleLifetimeScope`等、**シーン単位**の`LifetimeScope`全て。Connection(Mock/Real)・Repository・Serviceの実装をDIコンテナへ登録する構成ルート(Composition Root) | `Atlas.Domain`, `Atlas.Application`, `Atlas.Presentation`, `Atlas.Infrastructure`(base/Api/Mock全部), `Atlas.Navigation`, USN, Supplement, VContainer |

Presenterは`Atlas.Presentation`に属するが、`IXxxService`(Applicationのインターフェース)経由でしか
呼ばないため、Connectionの実体(Mock/Real)も、所持データがどのRepositoryにどう保持されているかも
意識しない(`Atlas.Domain`の型はEntityとしてのみ利用し、`Atlas.Infrastructure`系は一切参照しない)。

**`Atlas.Navigation`の分離**: `IScreenNavigator`/`ScreenNavigator`/`PageLifetimeScope<TViewDto>`は
特定の画面(`XxxPage`/`XxxPresenter`)の型を一切知らない汎用の画面遷移基盤であり、USNと
VContainerだけに依存する。`Atlas.Domain`にも`Atlas.Presentation`にも依存しないため、
`Atlas.Presentation`から独立したアセンブリ(`Atlas.Navigation`)として切り出す。
`Atlas.Presentation`側は`Atlas.Navigation`を参照する一方向の依存になる
(`XxxPageLifetimeScope`が`PageLifetimeScope<TViewDto>`を利用する)。

**`Atlas.DI`は構成ルート(Composition Root)専用の最上位アセンブリ**: `Atlas.DI`は
`Atlas.Domain`/`Atlas.Application`/`Atlas.Presentation`/`Atlas.Infrastructure`/`Atlas.Navigation`
を束ねてDIコンテナへ登録する場所と位置づけ、シーン単位の`LifetimeScope`(`RootLifetimeScope`
本体に加え、`HomeLifetimeScope`/`BattleLifetimeScope`等シーンロード時に生成される
スコープ)を**すべて**ここに置く。`RootLifetimeScope`が`IXxxConnection`のMock/Real実装・
`IXxxRepository`/`IXxxService`の実装を`Configure`内で登録するため`Atlas.Infrastructure`への参照が、`HomeLifetimeScope`が
`screenNavigator.PushPageAsync<HomePage>()`のように画面をPushするため`Atlas.Presentation`への
参照が、それぞれ必要になる。これを`Atlas.Presentation`内の例外として個別に扱うのではなく、
「他の全レイヤーに依存してよい最上位のアセンブリ」として`Atlas.DI`に一本化する。
`Atlas.Presentation`は画面単位の型(`XxxPage`/`XxxPresenter`/`XxxViewDto`/`XxxPageLifetimeScope`)
だけを持ち、`Atlas.Infrastructure`はもちろん`Atlas.DI`も参照しないため、依存方向は常に
`Atlas.DI` → `{Atlas.Domain, Atlas.Application, Atlas.Presentation, Atlas.Infrastructure, Atlas.Navigation}`
の一方向で、逆方向の参照は発生しない。

### C#スクリプトのフォルダ構成(`Atlas.Navigation`/`Atlas.Presentation`/`Atlas.DI`内)

`Atlas.Navigation`は画面固有の型を持たないため、直下にフラットに置く。

```
Assets/Scripts/Navigation/
  IScreenNavigator.cs
  ScreenNavigator.cs             -- IScreenNavigatorの実装(ScreenServiceの実装クラス名に揃える)
  ISceneNavigator.cs
  SceneNavigator.cs
  PageLifetimeScope.cs           -- PageLifetimeScope<TViewDto>基底クラス
```

`Atlas.DI`もシーン単位のスコープしか持たないため、直下にフラットに置く(シーン数だけ
ファイルが増えるが、1シーン1ファイルなのでサブフォルダは不要)。

```
Assets/Scripts/DI/
  RootLifetimeScope.cs           -- Bootstrapシーン。IXxxConnectionのMock/Real登録・IXxxRepository/IXxxServiceの登録
  HomeLifetimeScope.cs           -- Homeシーン
  BattleLifetimeScope.cs         -- Battleシーン
  SaveDataDirectory.cs           -- セーブデータの保存先の切り替え(Multiplayer Play Mode・スタンドアロンビルド用)
```

`Atlas.Presentation`はAddressablesのprefab配置(`Assets/Addressables/Views/{機能名}/`、
上記「データ受け渡し型の命名規則」参照)と同じ`{機能名}`でフォルダを切る。

- **Page**: `XxxPage`/`XxxPageLifetimeScope`/`XxxPresenter`/`XxxViewDto`は`{機能名}`直下に置く。
  その画面だけで使うView部品・表示用Dto・ロジック(`PachimonListView`/`PartySlotDto`/`PartyEditor`等)も同じ階層に置く
- **Modal**: `{機能名}`の下にModalごとのサブフォルダを切り、`XxxModal`/`XxxModalLifetimeScope`/
  `XxxPresenter`/`XxxViewDto`/`XxxResult`とそのModal専用の部品をまとめる(namespaceは`{機能名}`のまま)
- **部品が多い画面**: Pageの部品が多い場合は`Views/`サブフォルダにまとめてよい(`Battle/Views/`)
- **他機能と共用する部品**: 最初に作った機能のフォルダに置いたまま、他機能から参照する
  (`Party/`の`PachimonInfoView`/`PachimonCellView`/`PachimonInfoDtoBuilder`をScout・Battleからも使う)
- **特定の機能に属さない共通の型**: `Common/`に置く(`PaletteColor`/`PachimonTypeNames`等)

```
Assets/Scripts/Presentation/
  Common/                         -- 機能に属さない共通の型
  Home/                           -- 機能名(Addressablesの{機能名}と揃える)
    HomePage.cs
    HomePageLifetimeScope.cs
    HomePresenter.cs
    HomeViewDto.cs
    Matchmaking/                  -- Modal(1Modal = 1サブフォルダ)
      MatchmakingModal.cs
      MatchmakingModalLifetimeScope.cs
      MatchmakingViewDto.cs
  Scout/
    ScoutPage.cs
    ScoutPageLifetimeScope.cs
    ScoutPresenter.cs
    ScoutViewDto.cs
    ScoutCandidateListView.cs     -- ScoutPage専用の部品
    Confirm/
      ScoutConfirmModal.cs
      ...
  Battle/
    BattlePage.cs
    ...
    Views/                        -- BattlePageの部品(CommandView/SelfInfoView等)
    Forfeit/
    Result/
    Switch/
```

- Addressables側はprefab単体なので、Modalも含めて`Views/{機能名}/`直下にフラットに置く
  (`Views/Scout/ScoutConfirmModal.prefab`)。機能をまたいで使う部品のprefabは`Views/Parts/`に置く
  (`CellTemplate`/`PachimonInfoPanel`)。C#側だけModalのサブフォルダを切る非対称さは、
  prefabは1画面1ファイルなのに対しC#は1画面4〜5ファイルになるため(理由が異なるため統一しない)
- `IScreenNavigator`実装は`Atlas.Navigation`直下に置く(上記参照)

**検討したが採用しなかった案: 1画面ごとにサブフォルダを切る構成**(`Party/PartyEdit/PartyEditPage.cs`)。
当初はこの規則だったが、実際の機能は現状1機能1Pageで、Pageまでサブフォルダに入れると
機能名フォルダの直下が空になるだけだったため、Pageは機能名直下・Modalだけサブフォルダ、の現行構成にした。
1機能に複数Pageが入るようになった時点で再検討する。

## 実装検証済み(Bootstrap→Home→TitlePage)

以下は`Client/AtlasUnityProject`に最小実装(`Atlas.Presentation`/`Atlas.DI`/`Atlas.Navigation`
アセンブリ、`Bootstrap`/`Home`シーン、`TitlePage`)を作成し、Play Modeで実際に動作確認済み。

- `RootLifetimeScope`(Bootstrap)→`ISceneLoader.ChangeScene("Home")`→`HomeLifetimeScope`
  (`LifetimeScope.EnqueueParent`でRootの子として構築)→`IScreenNavigator.PushPageAsync`→
  USNがAddressablesから`TitlePage`prefabをInstantiate→`TitlePageLifetimeScope`
  (`EnqueueParent`でHomeの子として構築)→`RegisterEntryPoint<TitlePresenter>()`の副作用で
  `TitlePresenter.Initialize()`が自動実行→`view.Refresh(initialDto)`で初期表示、という
  一連の流れが実際に動くことを確認
- ボタンクリック(`OnClickAsObservable`購読)→`Refresh`での表示更新も、実際のUIクリック
  シミュレーションで確認済み
- 実装中に判明し設計を修正した点(いずれも上記「DIによる結線とライフサイクル」に反映済み):
  `ScreenNavigator`は`TPresenter`の具体型を知らないため`Resolve<XxxPresenter>()`は不可能
  だった(`RegisterEntryPoint`に変更)。`RegisterEntryPoint`は`IInitializable`等を実装しないと
  一度も構築されない(`XxxPresenter`に`IInitializable`を追加し、購読処理をコンストラクタから
  `Initialize()`へ移動)
- Supplementの`IMessageBroker`(ZeroMessenger実装)は`com.chinpangx.supplement.zeromessenger`
  という別パッケージ(`Assets/Supplement.ZeroMessenger/`)だったため、`com.chinpangx.supplement`
  とは別にmanifest.jsonへの追加が必要だった
- Supplementの`RegisterAddressablesLoader()`等VContainer統合拡張、USNのAddressablesアセット
  ローダー(`AddressableAssetLoaderObject`)は、それぞれ`USE_VCONTAINER`/`USN_USE_ADDRESSABLES`
  スクリプティング定義シンボルの有効化が必要だった(共に有効化済み)

## 未確定・今後決めること

- Battle終了後にHomeへ戻る際、対戦結果(勝敗・報酬)をどう引き継ぐか(シーンLifetimeScope経由で
  受け渡すか、`IBattleConnection`側の戻り値で完結させるか)は未検討
- アウトゲーム各画面(パーティ編成・スカウト・チャット等)のPresenter/ViewDto設計は
  本ドキュメントの対象外。画面ごとに実装時定義する
