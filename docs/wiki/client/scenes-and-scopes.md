---
title: "シーンとスコープ"
status: current
area: client
related: [client/navigation, client/screens, client/sign-in, master-data/pipeline]
code: [Client/AtlasUnityProject/Assets/Scripts/DI, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/_DIExtensions.cs, Client/AtlasUnityProject/Assets/Scripts/Application/BattleEntryStore.cs]
updated: 2026-10-17
---

# シーンとスコープ

`Bootstrap` / `Home` / `Battle` の3シーン。

```
Bootstrap(ビルド設定の起動シーン。Addressables の対象外。一度もアンロードしない)
  RootLifetimeScope: マスタの読み込み、通信の共通処理(ApiCore)、Repository・Service の DI 登録、エラーダイアログ
    │ 起動時の初期化が終わったら Home へ
    ▼
Home(Addressables)                               Battle(Addressables)
  HomeLifetimeScope                                BattleLifetimeScope
  USN の Page/Modal/Overlay のコンテナ     ⇄      USN の Page/Modal/Overlay のコンテナ
  タイトル・ホーム・パーティ編成・スカウト         選出・対戦・交代・投了・結果
```

- シーンの切り替え(Bootstrap → Home、Home ⇔ Battle)は `ISceneNavigator`、シーンの中の画面遷移は USN の Page/Modal で行う
  ([画面遷移](navigation.md))
- Bootstrap をアンロードしないことで、`RootLifetimeScope` を `DontDestroyOnLoad` 等を使わずに常駐させる
- 次のシーンを読み込んでから前のシーンを外す(切り替え中に何も無い瞬間を作らない)
- Home シーンは、サインインしていなければタイトルから、していればホームから始まる([サインイン](sign-in.md))
- Home → Battle への対戦情報(`BattleMatch`)の受け渡しは、Root 常駐の `BattleEntryStore` を通す(シーンを切り替えると Home 側のスコープは破棄されるため)
- 配置: Page/Modal の prefab は `Assets/Addressables/Views/{機能名}/`、機能をまたぐ部品の prefab は `Views/Parts/`、Home/Battle シーンは
  `Assets/Addressables/Scenes/`、Bootstrap は `Assets/Scenes/`
- タブ(`SheetContainer`)は使う画面が無いため入れていない

## 起動の流れ

1. `RootLifetimeScope` がサービスを登録する(`RegisterInfrastructure`・`RegisterGrpc`・対戦の接続・起動時の初期化)
2. `BootstrapEntryPoint` が起動時の初期化(`IBootInitializationTask`。いまはマスタの読み込み `MasterDataInitializationTask` だけ)を待つ
3. Home シーンへ切り替える

サインインは起動処理に入れず、タイトル画面で行う([サインイン](sign-in.md))。

## スコープ

VContainer のスコープは3段。

| 段 | 生存期間 | 登録するもの |
|---|---|---|
| Root(`RootLifetimeScope`) | アプリ | ローダー、暗号化セーブ、`IMessageBroker`、`ISceneNavigator`、`IErrorNavigator`、`BattleEntryStore`、Repository・Service・`ApiCore`・生成したラッパー、対戦のマッチング・接続のファクトリ、マスタ |
| シーン(`HomeLifetimeScope`/`BattleLifetimeScope`) | シーン | そのシーンの USN のコンテナと `IScreenNavigator`。Home はグローバルヘッダー(`GlobalHeaderView`・`GlobalHeaderPresenter`)も。`FindParent()` で Root を直接親にする |
| Push 単位の子スコープ | 画面の表示期間 | View・ViewDto・Presenter。フォークの `ScreenNavigator` が `CreateScope` で作るヘッドレスなスコープ |

- Presenter はコンテナを直接触らず `IScreenNavigator` で遷移する
- Page/Modal の prefab に `LifetimeScope` は置かない
- Service・Repository は Root のシングルトンで、特定の画面の表示期間に結び付かない。そのためメソッドは `CancellationToken` を受け取らず、
  途中でやめたい場合は Presenter 側で待つのをやめる(対戦の `IBattleConnection` はセッション単位のため例外)
- マスタ(`MemoryDatabase`)は起動時の初期化で読み込むため、Build の時点では登録できない。最初に要求されたときに読み込み済みのものを返す
  ファクトリで登録し、初期化より前に要求したら例外にして気づけるようにしている

## 同じ PC で2人分を動かすとき

セーブデータ(アカウント情報)の保存先を、起動しているインスタンスごとに分ける(`SaveDataDirectory`)。同じ保存先を読むと同じ
`device_id`(= 同じプレイヤー)になり、自分自身とマッチングしようとしてしまうため。

| 起動のしかた | 保存先 |
|---|---|
| Editor 本体 | 既定の `SaveData` の直下 |
| Multiplayer Play Mode の追加 Editor インスタンス | `SaveData/VP_<id>` |
| スタンドアロンビルド | `SaveData/Build`。起動引数 `-saveSlot N` で `SaveData/Build_N` |

どれも `persistentDataPath/SaveData` の下に作る。`Tools > Delete SaveData` で `SaveData` を消すと、すべてのインスタンスのアカウントを作り直せる。
