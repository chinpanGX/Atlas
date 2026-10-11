---
title: "画面の構成(View / Presenter)"
status: current
area: client
related: [client/navigation, client/assemblies, client/battle-screen, client/services-and-player-data]
code: [Client/AtlasUnityProject/Assets/Scripts/Presentation]
updated: 2026-10-11
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
- いまは Root に登録してあるだけで、使っているメッセージは無い。ホームのジェムの表示は、スカウトから戻ったとき(`WillPopEnterAsync`)に取り直している
- Push した画面から結果を受け取るのは `WaitForPopAsync` で行い、`IMessageBroker` は使わない

## 画面一覧

| 機能 | 画面 | 種類 | 内容 |
|---|---|---|---|
| タイトル | `TitlePage` | Page | 「スタート」でサインインし、ホームへ([サインイン](sign-in.md)) |
| ホーム | `HomePage` | Page | 所持ジェム、スカウト・パーティ編成・対戦への入口、開発用のジェム付与ボタン |
| | `MatchmakingModal` | Modal | マッチング待ち。キャンセルできる |
| パーティ編成 | `PartyPage` | Page | 所持個体の一覧と6つの枠。個体の能力と技(`PachimonInfoView`)を表示し、保存して戻る |
| スカウト | `ScoutPage` | Page | 紹介を受け、候補10体から選ぶ。候補をタップすると詳細を出し、同じ候補をもう一度タップすると確認へ進む。選ぶまで戻れない |
| | `ScoutConfirmModal` | Modal | 選ぶ候補の確認。入手に失敗したら候補を残し、もう一度タップすれば送り直せる |
| 対戦 | `BattlePage` ほか | Page / Modal | [バトル画面](battle-screen.md) |

いずれもサムネイルは、Presenter がマスタの `pachimon_id` を DTO に入れるだけで、View(`PachimonThumbnailView`・`PachimonFigureView`)が
`IAssetLoader` で `AddressDefinition.GetThumb(id)` を読み込む。読み込めるまではプレースホルダを出し、次の表示・破棄のときに前のものを解放する。
一覧のセルは `IObjectResolver.Instantiate` で複製する(複製したセルは Inject されないため)。
