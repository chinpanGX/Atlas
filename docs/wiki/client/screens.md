---
title: "画面の構成(View / Presenter)"
status: current
area: client
related: [client/navigation, client/assemblies, client/battle-screen, client/services-and-player-data]
code: [Client/AtlasUnityProject/Assets/Scripts/Presentation]
updated: 2026-10-17
---

# 画面の構成(View / Presenter)

「View のユーザー操作 → Presenter が購読 → Service を呼ぶ → 結果から ViewDto を作る → View を更新する」という一方向の流れにそろえる。

| 役割 | 型 | 内容 |
|---|---|---|
| View | `XxxPage` / `XxxModal`(USN の `Page`/`Modal` を継承) | ボタン等を `Observable` で公開し、`Refresh(dto)` で表示に反映するだけ。Service やマスタの型を知らない |
| Presenter | `XxxPresenter`(フォークの `IPresenter`。引数を受け取る画面は `IScreenWithArgs<XxxViewDto>`) | 具象の View と Service 等をコンストラクタで受け取り、View を購読する |
| 表示データ | `XxxViewDto` | Push 時の入力と、View の表示データを兼ねる |
| Pop 結果 | `XxxResult` | Presenter の `CompleteAsync` で返し、Push 元が `WaitForPopAsync` で受け取る |
| Service | `IXxxService`(Application、実装は Infrastructure) | 複数の画面から使う処理。Presenter はインターフェースで受け取る |

- View にインターフェース(`IXxxView`)は作らない。Presenter と View は同じ画面のための1対1の組で、抽象化しても差し替える場面がほぼ無いため。
  一方 Service は複数の画面で使い、テストで送り先を差し替えるため、インターフェースにする
  (参考: [Mirrativ の記事](https://tech.mirrativ.stream/entry/2023/09/22/112042)、[OutgameSample](https://github.com/mr-imada/OutgameSample))
- 依存は Presenter → View の一方向。View は Presenter を知らない
- ViewDto の粒度(画面全体を作り直すか、差分だけ渡すか)は画面ごとに決める。アウトゲームは全体、バトルは差分寄り
  (`BattlePage` は状態の `BattleUIStateDto` と、メッセージ・演出用のメソッドを分けている)
- **`InitializeAsync` が終わるまで開くアニメーションが始まらない**。通信のように時間がかかる処理(スカウトのバナー取得、バトルサーバーへの参加)は
  `InitializeAsync` で待たず、開始だけして先に画面を開く。また `InitializeAsync` の中でシーンの切り替えを待つと、切り替えが Push 自身の完了を
  待つためデッドロックする
- Presenter は `Dispose` で購読を解放するだけでよい(子スコープの破棄で呼ばれる。自分で呼ばない)
- 通信を待つ間の連打は `AwaitOperation.Drop` で捨てる

## 命名

- 画面ごとの型は画面名をプレフィックスにそろえる(`XxxPage`/`XxxModal`、`XxxPresenter`、`XxxViewDto`、`XxxResult`)
- Presenter には `[AssetAddress(AddressDefinition.Xxx)]` を付ける。`AddressDefinition` は Addressables から自動生成する定数で、Page/Modal を足したら生成し直す
- prefab のアドレスは、対応する View のクラス名と同じにする

## 画面をまたぐ通知

- 同じ画面の中の通知は、R3 の `Observable` を直接購読するだけにする(pub/sub は使わない)
- どの画面から・どの画面へと決まらない通知(トースト等)だけ、Supplement の `IMessageBroker` を使う。実装(`GlobalMessageBroker`)は型ごとの
  静的なチャンネルに委譲するだけなので、どのスコープで解決しても同じチャンネルにつながる。登録は Root で1回。メッセージは `struct` で、
  通知の内容を表す名前にする(画面名は付けない)
- いま使っているメッセージは `ItemQuantityChanged`(所持アイテムの数が変わった)だけ。`ItemRepository` が送り、グローバルヘッダーと育成画面が受けて所持数を出し直す
- Push した画面から結果を受け取るのは `WaitForPopAsync` で行い、`IMessageBroker` は使わない

## グローバルヘッダー

アウトゲームの画面に共通のヘッダー(戻る・画面名・ジェムと育成チケットの所持数・ショップ)。仕様は
[遊びの流れとアカウント](../game/flow.md)の「グローバルヘッダー」。

- Home シーンに1つだけ置き(`GlobalHeaderView`)、Home のシーンスコープに `GlobalHeaderPresenter` を登録する。Page ごとに持たないのは、
  画面の切り替えでヘッダーまで一緒に動かさず、所持数の更新も1か所で済ませるため
- シーンの中では `PageContainer` と `ModalContainer` の間に置く。Modal の背景がヘッダーも覆うので、マッチング待ちやスカウトの確認の間は
  ヘッダーを押せない
- ヘッダーを使う画面の Presenter は、`InitializeAsync` で `GlobalHeaderPresenter.Attach` し、戻り値(`GlobalHeaderHandle`)を購読と一緒に破棄する。
  ヘッダーは最後に Attach した(一番上に積まれた)画面の `GlobalHeaderOptions`(画面名・戻るを出すか・ショップを出すか)で表示する。
  Attach している画面が無い(タイトル)ときは出さない
- 戻るは、一番上の画面の `LeaveAsync` を呼んでから Pop する。`false` が返れば留まる(パーティ編成は、ここで変更を保存する)。
  確かめている間(保存の通信中)はヘッダーを押せなくする
- 戻るボタンには Supplement の `BackKeyReceiver` を付けている。Android の戻るボタン(Editor では Esc)で戻るボタンを押したことになる。
  Modal の背景がヘッダーを覆っている間・押せない間・ホーム(戻るを出さない)では反応しない
- 途中で抜けると進行が壊れる間は、`GlobalHeaderHandle.SetLocked(true)` でヘッダーを押せなくする(スカウトで候補を選んでいる間)
- 所持数は `IItemService` から読み、`ItemQuantityChanged` を受けたら出し直す
- ショップのボタンは、一番上の画面の上に `ShopPage` を Push する。ショップは `CanOpenShop = false` で Attach するので、ショップの中ではボタンを出さない

## 画面一覧

| 機能 | 画面 | 種類 | 内容 |
|---|---|---|---|
| タイトル | `TitlePage` | Page | 「スタート」でサインインし、ホームへ([サインイン](sign-in.md)) |
| ホーム | `HomePage` | Page | スカウト・パーティ編成・対戦・育成への入口 |
| | `MatchmakingModal` | Modal | マッチング待ち。キャンセルできる |
| パーティ編成 | `PartyPage` | Page | 所持個体の一覧と6つの枠。個体の能力と技(`PachimonInfoView`)を表示する。ヘッダーの戻る・ホームで離れるときと、「育成する」で育成画面を開く前に保存する |
| 育成 | `TrainingPage` | Page | 所持個体の一覧(パーティの印付き。一覧はパーティ編成の `PachimonListView` を使う)と、選んだ個体の技・ステータスのタブ。`TrainingArgs` で最初に選ぶ個体を受け取る。割り振りの調整は `TrainingStatAllocation` が持ち、「確定する」まで通信しない。確定の通信の間はヘッダーを押せない |
| | `TicketShortageModal` | Modal | 育成チケットが足りないときのダイアログ。「ショップへ」を選んだかを返し、育成画面がショップを Push する |
| | `DiscardAdjustmentModal` | Modal | 確定していない割り振りの調整があるまま、別の個体・別のタブ・別の画面へ移るときの確認。捨てるかを返す |
| | `MoveReplaceModal` | Modal | 技の枠に入れる技を、種族の技グループの技(`PachimonMoveLookup.GetLearnableMoves`)から選ぶ。いまの技・別の枠で覚えている技は薄くして理由を出す。選んだ技(`MoveReplaceResult`)を返し、付け替えの送信は育成画面が行う。「やめる」に `BackKeyReceiver` を付けている |
| スカウト | `ScoutPage` | Page | 紹介を受け、候補10体から選ぶ。候補をタップすると詳細を出し、同じ候補をもう一度タップすると確認へ進む。選ぶまでヘッダーを押せない |
| | `ScoutConfirmModal` | Modal | 選ぶ候補の確認。入手に失敗したら候補を残し、もう一度タップすれば送り直せる |
| ショップ | `ShopPage` | Page | 商品の一覧(`ListProducts` の結果を並べる)。ジェムが足りない商品は押せなくし、足りない数を出す。購入できたらトーストで知らせて残る。開発ビルドでは左に開発用のジェム付与を出す |
| | `ShopConfirmModal` | Modal | 購入の確認。買う前と後のジェム・アイテムの数を並べる。「やめる」に `BackKeyReceiver` を付け、戻るキーでも閉じる。購入の通信の間はヘッダーを押せない |
| 対戦 | `BattlePage` ほか | Page / Modal | [バトル画面](battle-screen.md) |

いずれもサムネイルは、Presenter がマスタの `pachimon_id` を DTO に入れるだけで、View(`PachimonThumbnailView`・`PachimonFigureView`)が
`IAssetLoader` で `AddressDefinition.GetThumb(id)` を読み込む。読み込めるまではプレースホルダを出し、次の表示・破棄のときに前のものを解放する。
一覧のセルは `IObjectResolver.Instantiate` で複製する(複製したセルは Inject されないため)。
