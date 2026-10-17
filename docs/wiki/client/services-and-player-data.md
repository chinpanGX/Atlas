---
title: "サービスと所持データ"
status: current
area: client
related: [conventions/player-diff, client/assemblies, client/sign-in, client/error-dialog, server/matchmaking]
code: [Client/AtlasUnityProject/Assets/Scripts/Application, Client/AtlasUnityProject/Assets/Scripts/Infrastructure, Client/AtlasUnityProject/Assets/Scripts/DI/_DIExtensions.cs, Client/AtlasUnityProject/Assets/Scripts/Domain]
updated: 2026-10-17
---

# サービスと所持データ

サーバーとの通信を受け持つ Service と、クライアントが持つ所持データ(Repository)を別の抽象に分ける。

| 抽象 | 置き場所 | 内容 |
|---|---|---|
| `IXxxService` | `Atlas.Application`(実装は `Atlas.Infrastructure`) | Presenter が使う処理。通信するものは、`.proto` から生成したラッパー(`PlayerApi`・`PartyApi`・`ScoutApi`・`ShopApi`・`MatchmakingApi`・`DebugApi`)を呼び、生成の型とアプリの型を詰め替える |
| `IXxxRepository` | `Atlas.Domain` | 手元の所持データの読み書き。`player_diff` の種別ごとに1つ。ほかに `IPlayerProfileRepository` |
| `IPlayerSession` | `Atlas.Application` | サインインしているか。タイトルに戻るときに手元の所持データを捨てる |
| `IBattleMatchMaker` / `IBattleConnectionFactory` | `Atlas.Application` | 対戦相手を探す / 対戦ごとの接続を作る(Mock と実サーバーの切り替え単位) |

## Service の一覧

| Service | 通信 | 内容 | 使う画面 |
|---|---|---|---|
| `ISignInService` | (下の Service 経由) | 未作成なら作ってからサインインし直す | タイトル |
| `IPlayerSignInService` | `PlayerApi` | サインアップ・サインイン。サインインの全件を各 Repository に入れる | (`ISignInService` から) |
| `IPlayerAccountService` | なし | プレイヤー ID・ニックネーム | ホーム |
| `IItemService` | なし | アイテムの所持数 | グローバルヘッダー・スカウト・ショップ・育成 |
| `IPachimonService` | なし | 所持個体(ステータスポイントの割り振りを含む) | パーティ編成・スカウト・育成 |
| `IPachimonMoveMappingService` | なし | 個体が覚えている技 | パーティ編成・バトル・育成 |
| `IPartyService` | `PartyApi` | 編成の取得と保存(全置き換え) | パーティ編成・育成(パーティの印) |
| `IPachimonMoveEditService` | `PartyApi` | 技の付け替え | 育成 |
| `IPachimonStatPointsEditService` | `PartyApi` | ステータスポイントの割り振り(割り振りの後の値を送る) | 育成 |
| `IScoutService` | `ScoutApi` | バナー一覧・紹介・選択 | スカウト |
| `IShopService` | `ShopApi` | 商品一覧・購入 | ショップ |
| `IDebugConnectService` | `DebugApi` | 開発用のジェム付与 | ショップ |
| `IBattleMatchMaker` | `MatchmakingApi` | 待機列に入り、成立するまで待つ | ホーム |

- 通信の共通処理は gRPC 基盤の `ApiCore` が受け持つ(トークンの付与・期限前の発行し直し・`UNAUTHENTICATED` のときの1回だけの再送、
  エラーの変換とダイアログ、RequestID と順番待ち、サーバー時刻・ゲーム内の日付、`player_diff` の反映)
- Service は成否(または結果の型)だけを返し、例外は投げない。失敗したときのダイアログ・画面遷移は `ApiCore` が済ませているので、
  呼び出し側は成否を見て抜けるだけにし、`try/catch` で通信のエラーを処理しない
- 画面で自分で処理したいエラー(サインインの `PLAYER_NOT_FOUND`)だけ、ラッパーの `handledCodes` に渡して受け取る
- Service は `player_diff` を知らない(反映は `ApiCore` が済ませている)。成功した後は Repository を読めばサーバー確定後の値が返る

## 所持データの反映

```
レスポンス ─→ ApiCore ─→ PlayerDiffApplier(基盤の IPlayerDiffApplier)
                            ├ ItemDiffApplier            → IItemRepository
                            ├ PachimonDiffApplier        → IPachimonRepository
                            ├ PachimonMoveMapDiffApplier → IPachimonMoveMapRepository
                            └ PartyDiffApplier           → IPartyRepository
           ─→ 呼び出し側(Service)に結果を返す
```

- `player_diff` は、種別ごとの `XxxDiffApplier` が `upserted`/`removed` を対応する Repository の `Upsert`/`Delete` に変換する
  (`PlayerDiffApplier` が振り分ける)。変わらない種別は `null` で届くので飛ばす
- `ApiCore` は、成功したレスポンスを呼び出し側に返す前に反映する
- サインインの全件(`snapshot`)は、`PlayerSnapshotApplier` が各 Repository を空にしてから入れる(置き換え)。全件には `removed` が無く、
  先に消さないとサーバーで無くなったものが残るため
- 汎用の Diff 型・汎用の Repository は作らず、種別ごとに `XxxDiffApplier` と Repository を1組ずつ足す
- Repository はメモリに持つだけでディスクには保存しない。サーバーの DB が正で、サインインで全件を取り直せるため
- `ItemRepository` は、所持アイテムが変わるたびに `IMessageBroker` で `ItemQuantityChanged` を送る。どの画面にいても所持数を出し直せるように
  するため(グローバルヘッダーが受ける)

## 対戦の接続

マッチング(APIサーバー)とバトル本体(MagicOnion)で生存期間が違うため分ける。

- `IBattleMatchMaker` は Root のシングルトン。実装の `BattleMatchMaker` は `JoinQueue` の後、1秒ごとに `GetQueueStatus` を呼ぶ。
  通信が失敗したら `null` を返し、キャンセルされたら `LeaveQueue` を呼んでから `OperationCanceledException` を投げる。`ApiCore` は1回の通信を
  途中で止める口を持たないので、キャンセルは状況の確認の合間で見る
- `IBattleConnection` は対戦ごとの接続。`IBattleConnectionFactory` を Root のシングルトンにし、Battle シーンのスコープがマッチング結果から
  都度生成し、シーンの終了で破棄する
- Bootstrap シーンの `RootLifetimeScope` の Inspector(`Use Battle Server`)で、実サーバー(`BattleMatchMaker`・`RealtimeBattleConnectionFactory`)と
  Mock(`MockBattleMatchMaker`・`MockBattleConnectionFactory`)を切り替える([テスト](testing.md))
