# 画面遷移パッケージ 設計書(ドラフト)

> 本書は Supplement のサブパッケージとして作る画面遷移パッケージの設計ドラフト。
> パッケージ作成後は Supplement リポジトリへ移管する。
> gRPC API基盤([feature-grpc-foundation.md](feature-grpc-foundation.md))とは別の提案として扱う。

- ステータス: ドラフト(確定事項と未確定事項が混在。各項目に明記)
- 出発点: Atlas の `Atlas.Navigation`(`Client/AtlasUnityProject/Assets/Scripts/Navigation/`)
- 参考:
  - DevelopmentBooster の `ScreenService`(View のプール)
  - UnityScreenNavigator(USN)1.8.0 のソースとデモ(`Assets/Demo/Core`)
  - Qiita「Unity Screen Navigatorを使ったModal画面の基盤設計をしてみる」

---

## 1. 目的

Page / Modal / シーンの遷移と、常に最前面に出る画面(システムダイアログ、Connecting、トースト、Loading)を、
アプリをまたいで使えるパッケージとして提供する。

## 2. 範囲と前提 【確定】

- **Supplement のサブパッケージ**として作る(`Supplement.ZeroMessenger` と同じ形)
- 依存: **USN、VContainer、UniTask**。非同期処理はすべて UniTask にする
- **アプリの要件に依存しない**。見た目・アニメーション・表示データの内容はアプリが用意する Prefab と `ViewDto` が決め、
  パッケージは呼び出しの順序と制御だけを持つ
- 画面遷移パッケージ用の設計書として、gRPC 基盤とは別に管理する

---

## 3. 全体構成

### 3.1 レイヤー構成

```
[最前面] システムレイヤー(USNの外。Bootstrapシーンに常駐。プールして再利用)
           ├─ システムダイアログ
           ├─ Loading(シーン遷移)
           ├─ Connecting
           └─ トースト
         USN ModalContainer(シーンごと。毎回生成して破棄)
[最背面] USN PageContainer (シーンごと。毎回生成して破棄)
```

- システムレイヤー内の重なり順(sortingOrder)は**設定で変えられる**ようにする。パッケージは上記の順を初期値として持つだけ
  【確定】
- 例: トーストのレイヤーを Loading より上にすれば Loading 中もトーストが見え、下にすれば隠れる。パッケージとして特別な処理はしない

### 3.2 入り口: `IScreenNavigator` 【確定】

- 入り口は **`IScreenNavigator` 1つ**にまとめる。`ISceneNavigator` も統合し、システムダイアログ用・トースト用などの
  別インターフェースは作らない
- `ScreenNavigator` は**アプリ全体で常駐**する(ルートのスコープに Singleton で1つだけ登録)。シーンごとに具象クラスが変わることはない

```csharp
public interface IScreenNavigator
{
    // シーン
    UniTask ChangeSceneAsync(string address, CancellationToken ct = default);

    // Page / Modal(USN)。parentScope省略時はRoot(3.4参照)
    UniTask<TPage> PushPageAsync<TPage, TViewDto>(TViewDto dto, LifetimeScope parentScope = null, ...);
    UniTask PopPageAsync(...);
    UniTask WaitForPopAsync(...);
    UniTask<TModal> PushModalAsync<TModal, TViewDto>(TViewDto dto, LifetimeScope parentScope = null, ...);
    UniTask PopModalAsync(...);

    // システムレイヤー
    UniTask<TResult> ShowSystemDialogAsync<TDialog, TViewDto, TResult>(TViewDto dto, CancellationToken ct)
        where TDialog : Component, ISystemDialog<TViewDto, TResult>;
    IDisposable ShowConnecting();
    void ShowToast(...);   // 引数の形は未確定(6.4)
}
```

(メソッドの詳細なシグネチャは実装時に確定する)

### 3.3 シーンのコンテナ登録 【確定】

`ScreenNavigator` が常駐になるため、Push 先となる USN のコンテナ(どのシーンの `PageContainer`/`ModalContainer` に
描画するか)を、シーンの読み込み・アンロードに合わせて登録・解除する仕組みを持つ。
**これは描画先コンテナの切り替えだけの仕組みであり、DI の親スコープとは無関係**(3.4 参照)。

```csharp
// シーン側(シーンの LifetimeScope またはシーンに置くコンポーネント)から呼ぶ
screenNavigator.AttachScene(pageContainer, modalContainer);   // 読み込み時
screenNavigator.DetachScene(pageContainer, modalContainer);   // アンロード時
```

- `AttachScene` / `DetachScene` はシーンの仕組みから呼ぶものなので、画面側が使う `IScreenNavigator` には含めない(実装クラスのみ)
- **シーンの切り替え中(コンテナが登録されていない間)に `PushPageAsync` 等が呼ばれたら例外にする**
  - 切り替え中は Loading が前面にあり、その間に画面を開く正しい理由がほぼないため
  - 待たせる方式にすると、前のシーン向けの要求が次のシーンで実行される危険がある

### 3.4 画面(Page/Modal)の LifetimeScope の親 【確定】

Supplement が保証するのは次の1点だけ。**それ以上の Scope 階層の設計(シーン単位・機能単位でまとめる等)は
アプリ側の責任とし、パッケージはその存在を知らない・管理しない。**

> Push された Page / Modal には専用の LifetimeScope が1つ作られ、View と Presenter はこの Scope の
> 生存期間に紐づく(Push で生成、Pop で破棄される)。

- 親スコープは **既定で Root**。`PushPageAsync`/`PushModalAsync` の任意引数 `parentScope` で明示的に指定できる
  (3.2 のシグネチャ参照)
- 複数の画面で状態を共有したい、特定の機能でだけ生きる Service を使いたい、といった要望は、
  **アプリ側が自分で(VContainer の `CreateChild` 等で)中間の LifetimeScope を作り、`parentScope` に渡すことで実現する**。
  Supplement 側に「シーンスコープ」「機能スコープ」のような名前付きの概念・状態管理は持たない
- 画面の Scope は `FindParent()` で親スコープを直接返す実装にする(`LifetimeScope.EnqueueParent` の共有スタックは使わない。
  シーン読み込み完了と画面の同時 Push が競合してスタックが壊れる不具合を避けるため)
- 検討の経緯は [feature-bootstrap-foundation.md](feature-bootstrap-foundation.md) 3 章(Scope構成)を参照

---

## 4. シーン遷移 【確定】

```
ChangeSceneAsync(address)
  → Loading 表示
  → 現在のシーンの登録を解除(DetachScene)
  → 前のシーンをアンロード
  → 次のシーンを読み込む(進捗を Loading に渡す)
  → 次のシーンが登録(AttachScene)
  → Loading 非表示
```

- シーンの読み込みには Supplement.Loader(`ISceneLoader`)を使う(現行の `SceneNavigator` と同じ)
- Loading はシステムレイヤー(Bootstrap シーンに常駐)にあるため、シーンの切り替え中も表示し続けられる

---

## 5. Page / Modal

### 5.1 基本方針 【確定】

- USN の `PageContainer` / `ModalContainer` を使い、**毎回生成して破棄する(再利用しない)**
- 現行 `Atlas.Navigation` の仕組みを引き継ぐ
  - Push 時に `ViewDto` を渡し、Prefab の子 LifetimeScope(`PageLifetimeScope<TViewDto>`)を Build して Presenter を生成する。
    親スコープの決め方は 3.4 参照
  - 結果を返す画面(`ResultPage<TResult>` / `ResultModal<TResult>`)
  - 遷移の直列化(`TransitionQueue`)。コンテナ単位で持つ
- DLC のダウンロード画面のような専用の画面は、Modal として作る(システムレイヤーには入れない)

### 5.2 画面の型と ViewDto の型の結び付け 【確定】

現行の `PushModalAsync<TModal, TViewDto>` は、`TModal` と `TViewDto` の組み合わせを間違えてもコンパイルが通り、
実行時に `lts is PageLifetimeScope<TViewDto>` の判定が外れて **ViewDto が渡されないまま画面が開く**。
画面のクラスに受け取る ViewDto の型を宣言させ、組み合わせの誤りをコンパイル時に検出する。

```csharp
// 仮称。名前は実装時に確定する
public interface IScreenWithDto<TViewDto> where TViewDto : class { }

public sealed class ScoreModal : Modal, IScreenWithDto<ScoreViewDto> { }

UniTask<TModal> PushModalAsync<TModal, TViewDto>(TViewDto dto, ...)
    where TModal : Modal, IScreenWithDto<TViewDto>
    where TViewDto : class;
```

(Qiita 記事の `ModalBase<TParam>` と同じ考え方)

### 5.3 同じシーン内での読み込み待ち(参考)

USN は**次の Page の `WillPushEnter` が終わるまで遷移を始めない**。
そのため、同じシーン内で重い Page を開く前の待ち時間は、システムレイヤーの Loading を使わずに
「読み込み用の Page を `stack: false` で積み、次の Page の `WillPushEnter` で読み込む」方法でも対応できる
(USN デモの `LoadingPage` と同じやり方)。

---

## 6. システムレイヤー

### 6.1 基本方針 【確定】

- 常に最前面に出る画面(システムダイアログ、Connecting、トースト、Loading)は、**USN の外で管理し、プールして再利用する**
- Bootstrap シーンに常駐させ、アプリの起動中ずっとプールを保持する
- **画面ごとの LifetimeScope と Presenter は持たせない**。`ViewDto` は View が直接受け取る
- USN の遷移とは独立しているため、Page の遷移アニメーション中でもすぐに表示できる

USN の外にする理由(調査結果の詳細は 8 章):

| 課題 | システムレイヤーでは |
|---|---|
| USN は生成・破棄を差し替えられない | USN を通さないので関係ない |
| USN の `AfterLoad` を再度呼ぶとライフサイクルが重複して呼ばれる | USN のライフサイクルを使わない |
| `LifetimeScope.Parent` が最初の Build で固定され、シーンをまたいでプールできない | 画面ごとの LifetimeScope を持たないので関係ない |
| USN は遷移中の Push / Pop を例外で拒否する | USN の遷移と独立している |

### 6.2 システムダイアログ 【確定】

- **重なったら順番に1つずつ表示する**(順番待ち)
- パッケージが担当するのは、順番待ち・プール・最前面への表示だけ
- **View は押されたボタンの番号(種類)だけを返す**。結果の型はダイアログの種類ごとに固定する
- **ボタンの番号を呼び出し側の値に変換するのはアプリのビルダー**。ビルダーや翻訳キーの扱いはアプリ側の実装

```csharp
// パッケージ: アプリのダイアログ Prefab が実装する
public interface ISystemDialog<in TViewDto, TResult>
{
    UniTask<TResult> ShowAsync(TViewDto dto, CancellationToken ct);   // 押されるまで待って結果を返す
    UniTask HideAsync(CancellationToken ct);
}

// パッケージ: IScreenNavigator の入り口
UniTask<TResult> ShowSystemDialogAsync<TDialog, TViewDto, TResult>(TViewDto dto, CancellationToken ct)
    where TDialog : Component, ISystemDialog<TViewDto, TResult>;
```

```csharp
// アプリ側の例: View は押されたボタンの番号を返す
public sealed class CommonDialog : MonoBehaviour, ISystemDialog<CommonDialogViewDto, int> { ... }

// アプリ側の例: ビルダーが結果の型を持ち、番号を呼び出し側の値に変換する(キャスト不要)
public sealed class CommonDialogBuilder<TResult>
{
    private readonly List<TResult> values = new();

    public async UniTask<TResult> ShowAsync(CancellationToken ct)
    {
        var index = await screenNavigator.ShowSystemDialogAsync<CommonDialog, CommonDialogViewDto, int>(viewDto, ct);
        return values[index];
    }
}
```

- `TDialog` の型ごとに Prefab を登録してプールする。どの Prefab を使うかはアプリが登録する
- 表示のバリエーション(通常サイズ / 大きいサイズ等)は、アプリが別の `TDialog` を用意して対応する
- アプリのビルダーが `ShowSystemDialogAsync` と `PushModalAsync` のどちらを呼ぶかを選べば、同じビルダーで一般的な確認ダイアログ(USN の Modal)も開ける

参考: アプリ側のビルダーの実装で気を付ける点(実務の実装イメージから)

- 結果の型をビルダー全体で1つにする(ボタンごとの `T` と `Show<TResult>` を分けると、食い違いが実行時まで分からない)
- ビルダーは呼び出しごとに新しく作る(1つのインスタンスを使い回すと、同時に組み立てたときに内容が混ざる)
- 不正な組み立て(同じ種類のボタンの重複など)はログではなく例外で止める

### 6.3 Connecting 【確定】

旧称「通信中のインジケーター」。

```csharp
using (screenNavigator.ShowConnecting())
{
    await connection.SignInAsync();
}   // Dispose で参照カウントを1減らし、0になったら非表示
```

- **参照カウント**: 複数の通信が同時に走った場合は、すべて終わったら消す
- **表示までの遅延**: インスペクタで設定する(初期値 0.3 秒)。遅延の間に終われば一度も表示しない
- **最低表示時間**: インスペクタで設定する(初期値 0 = 無効)。表示直後に終わった場合のちらつきを防ぐ
- **タップの遮断は行わない**。遮断は TapGuard(7 章)の役割とする

### 6.4 トースト 【確定】

- 基本の入り口は **`IScreenNavigator.ShowToast()`**(IDE の補完で見つけやすく、引数の誤りもコンパイル時に分かる)
- メッセージ(Supplement の `IMessageBroker`)での受け取りは、画面遷移パッケージを参照したくない層から通知するための**任意の機能**として用意する
  - 常駐の受信サービスをルートのスコープに EntryPoint として登録し、受け取ったら `ShowToast` を呼ぶ
  - `IMessageBroker` の制約により、メッセージは `struct` にする
  - パッケージは `Supplement.Core.IMessageBroker` の抽象にだけ依存し、実装(`GlobalMessageBroker` 等)はアプリが登録する
- 複数を同時に表示でき、表示数に上限を設ける

### 6.5 Loading 【確定】

- `ChangeSceneAsync` の中で自動的に表示・非表示にする(4 章)
- **進捗を渡す口(`SetProgress`)を用意する**。使うかどうかはアプリの Loading 画面が選ぶ(使わない場合は何もしない実装にする)
- 最低表示時間をインスペクタで設定できるようにする(初期値 0 = 無効)

```csharp
// 案: アプリが Loading 画面の Prefab で実装する
public interface ILoadingView
{
    UniTask ShowAsync(CancellationToken ct);
    UniTask HideAsync(CancellationToken ct);
    void SetProgress(float progress);   // 0〜1
}
```

### 6.6 未確定の論点

1. トースト・Connecting・Loading の View の約束事(インターフェースの詳細、`ShowToast` の引数の形)。
   方針は「`ViewDto` の型はアプリが決め、パッケージは表示の制御だけ」でシステムダイアログと揃える
2. システムダイアログで、ボタン以外の方法で閉じられた場合(Android の戻るボタン等)と `ct` によるキャンセル時の扱い
3. プールの初期生成数(Prewarm)と上限
4. Prefab の登録方法(インスペクタ / ScriptableObject 等)

---

## 7. TapGuard(Supplement の別機能) 【未確定】

画面遷移とは独立した汎用の入力制御のため、**画面遷移パッケージではなく Supplement の一機能として作る**(画面遷移パッケージからは参照しない)。

### 7.1 解決したい問題

| 問題 | 起きること |
|---|---|
| 通信中のタップ | 通信の結果を待っている間に別の操作が走る |
| 連打 | 同じボタンの処理が終わる前に何度も走る |
| 同時押し | 2つのボタンを同じフレームで押し、Modal が2つ開く等 |

### 7.2 案

- `using` で囲める形にする

```csharp
using (tapGuard.Block())   // 参照カウント
{
    await connection.SignInAsync();
}

// ボタン: 押されたらガードを取り、処理が終わるまで他の入力を捨てる
button.BindGuarded(tapGuard, async ct => { ... });   // 仮称
```

- 連打と同時押しは「**ボタンを押したら処理が終わるまでアプリ全体で1つのガードを取り、取られている間の入力は捨てる**」ことで同時に解決する

### 7.3 未確定の論点

1. 止め方: ガード経由のボタンは自分で入力を捨てられる。それ以外の UI(スクロールビュー等)も止めるなら最前面に透明な全画面ブロッカーが必要
2. ガードの最中でも押せる必要がある画面(通信エラーのシステムダイアログ等)の扱い: ボタンごとに無視を指定するか、ブロッカーより上のレイヤーに置くか
3. 処理が終わった後もしばらくガードを持つ設定(初期値 0)を用意するか
4. R3 への依存: Supplement 本体に入れるか、R3 用の拡張を別サブパッケージにするか

---

## 8. 調査結果: USN での Page / Modal の再利用

Page / Modal 自体をプールして再利用する方式(DevelopmentBooster の `ScreenService` が View をプールしているのと同じ方式)を検討した結果。
**システムレイヤー以外の Page / Modal では再利用しない**と判断した(5.1)。

### 8.1 ScreenService の仕組み(参考)

- プールするのは View(画面の GameObject)だけ。Presenter と DI のスコープは表示のたびに新しく作り、`InjectGameObject` で注入し直す
- 閉じたら非アクティブにしてプール用のルート(`DontDestroyOnLoad`)の下に移す
- 画面遷移の仕組みを自作しており、USN は使っていない

### 8.2 USN で再利用できない理由

| 箇所 | 内容 |
|---|---|
| 生成 | 各コンテナの private メソッド(`LoadPage` 等)内で `Instantiate(assetLoadHandle.Result)` している |
| 破棄 | private な `AfterPopRoutine` / `AfterPushRoutine`(stack:false)/ `OnDestroy` 内で `Destroy` している |
| 差し替え | `PageContainer` / `ModalContainer` / `SheetContainer` は `sealed` |
| `IAssetLoader` | 差し替えられるのは Prefab の読み込みだけ。結果を USN が `Instantiate` で複製する |
| 再表示時の初期化 | `Page.AfterLoad`(Modal も同じ)は `internal` で、呼ぶたびに `_lifecycleEvents.AddItem(this, 0)` を実行する。`CompositeLifecycleEvent.AddItem` は重複を確認しないため、再利用するとライフサイクルのコールバックが重複して呼ばれる |
| Preload | 保持するのは Prefab(アセット)だけ。`Instantiate` は毎回行われる |

- GitHub の issue / PR を「pool」「reuse / cache / instance」で検索したが、関連する要望・機能はなかった
- 補足: `ModalContainer.ReleasePreloaded` が辞書からキーを削除していない(`PageContainer` 側は削除している)。解放後に同じ Modal を再度 Preload すると例外になる

### 8.3 再利用する場合に USN 以外で必要になる対応

- **VContainer**: Prefab 上の `LifetimeScope` は、戻すときに `DisposeCore()`(`Dispose()` は GameObject まで破棄する)、再利用時に `Build()`。
  `LifetimeScope.Parent` は最初の Build で固定されるため、プールの寿命はシーン以下にする必要がある
- **現行 Atlas.Navigation**: `WaitForPopAsync` と `ScreenResultCompletion` が `destroyCancellationToken` を合図にしているため、表示ごとのトークンへの置き換えが必要。
  `ResultModal` / `ResultPage` の `completion` はインスタンスごとに1回しか作られず、再利用すると前回の結果が返る
- **View**: `OnDestroy` の後始末を「プールに戻すとき」に移し、表示のたびに Presenter が全体を描き直す規約が必要

### 8.4 将来一般の Modal も再利用したくなった場合

USN をフォークし、コンテナの生成・破棄を差し替える拡張ポイントの追加と `AfterLoad` の再表示対応を行う(USN は MIT)。
上流への PR も並行して検討する。現時点では最後の手段として残す。

---

## 9. 現行 Atlas.Navigation からの移行時の作業 【未確定】

1. 名前空間とアセンブリ名(`Atlas.Navigation`)を汎用の名前にする
2. コメント中の Atlas 設計書への参照をパッケージの README 等へ移す
3. `InternalsVisibleTo("Atlas.Navigation.Tests")` を変更し、テストをパッケージに含める
4. `TransitionQueue` と Navigation のテストが作業中(未コミット)のため、落ち着いてからパッケージ化する
5. 依存の解決: UPM の `package.json` の `dependencies` には git URL の依存を書けない。
   VContainer / UniTask は OpenUPM にある。USN が OpenUPM で入手できるかは要確認。できなければ利用側で個別に導入する前提を README に書く

---

## 10. 確定事項・未確定事項一覧

### 確定

| # | 項目 | 内容 | 節 |
|---|---|---|---|
| 1 | 置き場所 | Supplement のサブパッケージ | 2 |
| 2 | 依存 | USN / VContainer / UniTask。非同期はすべて UniTask | 2 |
| 3 | 方針 | アプリの要件に依存しない。見た目と ViewDto の内容はアプリが決める | 2 |
| 4 | 入り口 | `IScreenNavigator` 1つ(`ISceneNavigator` を統合) | 3.2 |
| 5 | 常駐 | `ScreenNavigator` はアプリ全体で常駐し、具象クラスは1つ。シーンは描画先コンテナを登録・解除する | 3.2, 3.3 |
| 6 | シーン切り替え中の遷移要求 | 例外にする | 3.3 |
| 6.1 | 画面のScopeの親 | Supplement は「PushでScope生成・PopでDispose」だけを保証する。親は既定でRoot、Push時に`parentScope`で明示指定可能。それ以上のScope階層(シーン単位・機能単位)はアプリ側の責任とし、Supplementは概念として持たない | 3.4 |
| 7 | Page / Modal | USN で毎回生成して破棄する | 5.1 |
| 8 | 型の結び付け | 画面の型と ViewDto の型の組み合わせをコンパイル時に検査する | 5.2 |
| 9 | システムレイヤー | USN の外で管理し、常駐・プールして再利用する | 6.1 |
| 10 | システムダイアログ | 順番に1つずつ表示。View は番号を返し、値への変換はアプリのビルダー | 6.2 |
| 11 | Connecting | 参照カウント、表示までの遅延と最低表示時間をインスペクタで設定、タップ遮断はしない | 6.3 |
| 12 | トースト | `ShowToast()` が基本。メッセージでの受け取りは任意の機能 | 6.4 |
| 13 | Loading | `ChangeSceneAsync` 内で表示。進捗を渡す口を用意 | 6.5 |
| 14 | 重なり順 | 設定で変更可能。パッケージは初期値を持つだけ | 3.1 |
| 15 | TapGuard | 画面遷移パッケージではなく Supplement の別機能として作る | 7 |

### 未確定

| # | 項目 | 節 |
|---|---|---|
| 1 | トースト・Connecting・Loading の View の約束事の詳細 | 6.6 |
| 2 | システムダイアログのボタン以外での閉じ方・キャンセル時の扱い | 6.6 |
| 3 | プールの初期生成数と上限、Prefab の登録方法 | 6.6 |
| 4 | TapGuard の詳細(止め方、例外の画面、待ち時間、R3 依存) | 7.3 |
| 5 | 現行 Atlas.Navigation からの移行作業、USN の依存解決 | 9 |
