---
title: "通信エラーのダイアログ"
status: current
area: client
related: [conventions/errors, client/sign-in, client/services-and-player-data]
code: [Client/AtlasUnityProject/Assets/Scripts/Presentation/Common/ErrorDialog.cs, Client/AtlasUnityProject/Assets/Scripts/Presentation/Common/ErrorMessages.cs, Client/AtlasUnityProject/Assets/Scripts/Navigation/ErrorNavigator.cs, Client/AtlasUnityProject/Assets/Prefabs/ErrorDialog.prefab]
updated: 2026-10-11
---

# 通信エラーのダイアログ

APIサーバーとの通信のエラーの扱いは gRPC 基盤の `ApiCore` と `DefaultCommonErrorHandler` が決める。Atlas が持つのは次の3つだけ。

| Atlas が持つもの | 型 |
|---|---|
| ダイアログの見た目 | `ErrorDialog`(基盤の `IErrorDialogView` の実装) |
| エラーの後の画面遷移 | `ErrorNavigator`(基盤の `IErrorNavigator` の実装) |
| 文言 | `ErrorMessages` |

## ボタンと遷移

エラーは domain と reason の組(`ErrorCode`)で見分け、既定の action(`Shared/errors.toml` と基盤の予約エラー)でボタンが決まる。

| action | ボタン | その後 |
|---|---|---|
| stay | OK | 画面に留まる |
| retry | 再試行・タイトルへ | 再試行は同じ RequestID で送り直すので、サーバーで処理が終わっていても二重には実行されない |
| home | ホームへ | Home シーンを読み直す |
| title | タイトルへ | `ApiCore` がセッション(トークン・順番待ち)を終わらせた後、`ErrorNavigator` が手元の所持データを捨て(`IPlayerSession.SignOut`)、Home シーンを読み直す(サインインしていないのでタイトルから始まる) |
| update | ストアへ | ストアにはまだ出していないので、ログを出すだけ |

## 置き場所と文言

- `ErrorDialog` は Bootstrap シーンの最前面の Canvas(`Assets/Prefabs/ErrorDialog.prefab`)に置く。USN の Modal にしないのは、
  シーンの切り替え中やシーンのスコープが無いときにも出せるようにするため
- 文言は `ErrorMessages` の表で引き、知らないエラーは「予期しないエラー」として出す。メンテナンスは終了予定を端末の時刻で添える
- 業務エラーを足したら、`ErrorMessages` に文言を足す
- 対戦(MagicOnion)の参加失敗・切断は対象外で、`BattlePresenter` が扱う([バトル画面](battle-screen.md))

## まだ無いもの

- 通信中の Loading(基盤の入力ブロックのフックを使う)。すべての通信で自動にするかは未確定([未確定・保留](../open-questions.md))
- ストアのページ
