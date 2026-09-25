# メモ: DIのScope構成 / Real-Mock切り替え / TestRunnerでの個別登録

> これは設計書ではなく**メモ**。[feature-bootstrap-foundation.md](feature-bootstrap-foundation.md)(BootLoader)を
> 検討する過程で出てきた、DIのScope構成に関する検討内容を、今後のアプリ改善のために残しておくもの。
> Atlasで今すぐ採用するわけではない。直近では、gRPC通信基盤に置き換えるサンプルプロジェクト
> ([feature-grpc-foundation.md](feature-grpc-foundation.md)、新規リポジトリ)で使う可能性がある。
>
> 画面遷移パッケージ側で決まった「Push時に任意の `parentScope` を渡せる。Supplementは
> "PushでScope生成・PopでDispose"だけを保証し、それ以上のScope階層は概念として持たない」
> という方針([feature-screen-navigation-package.md](feature-screen-navigation-package.md) 3.4)と対応させて読むこと。

- 出発点: Atlas の `Scripts/DI/`(`RootLifetimeScope` / `HomeLifetimeScope` / `BattleLifetimeScope`)

---

## 1. 現状(Atlas)の構成

```
RootLifetimeScope(Bootstrapシーンに配置。アプリ生存期間中ずっと存在)
 ├─ HomeLifetimeScope(Homeシーン。FindParent()でRootを直接探す)
 │   └─ 画面ごとのScope(Page/Modal。EnqueueParentでHomeLifetimeScopeを親に)
 └─ BattleLifetimeScope(Battleシーン。FindParent()でRootを直接探す)
     └─ 画面ごとのScope(Page/Modal)
```

- `RootLifetimeScope` に Repository / Service / Connection 等を Singleton 登録している
- `HomeLifetimeScope` / `BattleLifetimeScope` は `PageContainer` / `ModalContainer` の登録と、
  `IScreenNavigator` のシーンごとの登録、シーン固有の EntryPoint(`HomeEntryPoint` 等)を持つ
- `BattleLifetimeScope` は対戦単位の `IBattleConnection`(1対戦=1接続)をここで Singleton 登録している
- テスト用の `TestRootLifetimeScope` が `RootLifetimeScope` を**継承**し、
  `ConfigureAuthConnections` / `ConfigureBattleConnections` を上書きして Mock に差し替えている
  (BootstrapTest シーンに配置)
- `RootLifetimeScope.EnqueueParent` の共有スタックに起因する不具合が過去に発生している
  (シーン読み込み完了のタイミングと、シーン自身の EntryPoint が始める Push の入れ子が競合し、
  スタックが壊れて親を誤認する)

---

## 2. Scope構成の見直し案

### 2.1 方針

- **シーン単位の LifetimeScope を廃止する**。理由:
  - シーンが増えるたびに、Application/Infrastructure の Service/Repository の登録をどのシーンの
    Scope で行うか判断する必要があり、**登録漏れが起きやすい**
  - シーンの Scope が無くなれば、Configure は基本的に **Root 1箇所** で行うことになり、
    「どこに何を登録したか」が一元化される
- 構成は **Root → 画面ごとの Scope** の2階層にする

```
Root(VContainerSettingsから生成。DontDestroyOnLoad)
 │  Application/Infrastructure の Service・Repository・Connection をすべて登録
 │  AssetLoader、MasterData(MemoryDatabase)、IScreenNavigator + システムレイヤー、BootLoader
 │
 └─ 画面ごとの Scope(Page/Modal。ScreenNavigator が開くたびに作る。親は直接 Root)
```

- シーンの PageContainer/ModalContainer の登録(描画先の切り替え)は残るが、**DIのScopeとしては存在しない**
- 画面ごとの Scope の親は `FindParent()` で直接渡す(`EnqueueParent` は使わない)。
  現行の `EnqueueParent` 由来の不具合(1章参照)を踏まえた判断
- シーン単位・機能単位でまとめたい Service がある場合は、Supplement側で「シーンスコープ」「機能スコープ」のような
  概念は用意せず、**アプリ側が自分で中間の LifetimeScope を作り、Push時に `parentScope` として渡す**ことで対応する
  (画面遷移パッケージの方針と対応)

### 2.2 Root は VContainerSettings を利用する

- Root は Bootstrap シーンに置いたオブジェクトではなく、**VContainerSettings に登録したプレハブ**にする
- 最初に読み込まれたシーンで自動生成され、`DontDestroyOnLoad` で常駐する(`VContainerSettings.GetOrCreateRootLifetimeScopeInstance`)
- 親を指定していない Scope は自動で Root を親にする(`LifetimeScope.GetRuntimeParent` の最後の候補)ため、
  シーンの Scope が `FindParent()` で Root を探す現行の実装は不要になる

### 2.3 起動処理(BootstrapEntryPoint)の扱い

- Root はどのシーンから再生を始めても生成されるため、**起動処理(マスター読み込み→サインイン→最初のシーンへ)を
  Root には置けない**(Homeシーンから再生を始めたときに二重に走る)
- マスターデータの読み込みは BootLoader([feature-bootstrap-foundation.md](feature-bootstrap-foundation.md))の
  初期化タスクとして Root で実行する
- サインインは初期化タスクにしない。Connection に依存し、失敗時にダイアログ等の画面対応が必要なため、
  Bootstrap シーン側の起動処理(BootLoader の完了を待った後)で行う

### 2.4 シーン/機能単位で生きるものの扱い

- `BattleLifetimeScope` の `IBattleConnection`(1対戦=1接続。対戦の終了時に破棄したい)のように、
  シーンや機能の単位で生存期間を持たせたいものが、シーンの Scope が無くなることで置き場所を失う
- 案1: VContainer の Scope 機能を使わず、該当の Service が自分でインスタンスを保持し、
  対戦の開始/終了に合わせて自前で生成・Dispose する
- 案2: アプリ側が対戦開始時に中間の LifetimeScope を自分で作り(`CreateChild` 等)、
  `IBattleConnection` をそこに登録した上で、Battle の Page/Modal を Push するときに `parentScope` として渡す(2.1参照)
- 設計は個別の機能(対戦の接続)を実装するタイミングで詰める

---

## 3. Real/Mock の切り替え案

### 3.1 方針

- **継承によるクラスの差し替えではなく、Configure内でメソッドを分ける**方式にする
  (現行の `TestRootLifetimeScope` による継承は廃止する)

```csharp
protected override void Configure(IContainerBuilder builder)
{
    ConfigureShared(builder);            // Real/Mockで変わらない登録

    if (useMock)
        ConfigureMockConnections(builder);
    else
        ConfigureRealConnections(builder);
}
```

- 判定(`useMock` 相当)は Build(Configure)の時点で確定している必要がある(Root は自動生成されるため)。
  開発中の切り替えはインスペクタの設定、PlayModeテストはテスト用シーンからの起動を検知して判定する
  (現行の `useBattleServer` と同様の考え方)

### 3.2 TestRunner での差し替え

- Root の継承やシーンの差し替えは行わない
- **PlayModeテストは、必要な Service だけを個別に登録する**方式にする
  (Root の Configure には手を入れず、テスト側が Mock を上書き登録する、または子の検証用 Scope を作る)
- 具体的な実現方法(テストコードから Root の登録にどう割り込むか)は未検討

### 3.3 未検討の論点

1. テストが個別登録を行う具体的な仕組み(上書き登録の方法、テスト専用の Installer 等)
2. Root が一度生成されると再生成しない(`DontDestroyOnLoad`)ため、**テスト間で状態が引き継がれる**問題への対応
   (Repository のキャッシュ、アクセストークン等のリセット。[feature-bootstrap-foundation.md](feature-bootstrap-foundation.md)の
   `IReinitializable` で解消できる可能性がある)
