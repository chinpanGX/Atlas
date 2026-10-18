---
title: "画面遷移"
status: current
area: client
related: [client/screens, client/scenes-and-scopes, tech-stack]
code: [Client/AtlasUnityProject/Assets/Scripts/Navigation, UnityScreenNavigator]
updated: 2026-10-18
---

# 画面遷移

フォークの USN の `IScreenNavigator` をそのまま使う(Atlas 側に独自の抽象は置かない)。定義はフォークのソースと README。

## Push と Pop

- **Push**: 引数のある画面の Presenter は `IScreenWithArgs<XxxViewDto>` を実装し、`PushXxxAsync<TPresenter, TArgs>(args)` で開く
  (型の組み合わせはコンパイル時に検査される)。引数の無い画面は `IPresenter` だけを実装する。`stack: false` で直前の Page を破棄する
  (戻る必要の無い遷移で使う)
- **Pop 結果**: 結果を返す画面の Presenter は `CompleteAsync()` で結果を返し、Push 元は `WaitForPopAsync<TResult>` で受け取る。
  結果の型は実行時に検査される(`object` で返すため、型が違うと `InvalidCastException`)。ボタン以外で閉じた場合(背景タップ、シーンごと破棄)も
  待機は終わり、その時点の値を返す
- **二重に Pop しない**のはアプリ側の責務。ボタンは `Take(1)` で1回だけ受け付ける。Push 元からも閉じる画面(交代選択・選出)は、
  最初の結果を優先して2回目以降は進行中の Pop を待つだけの `CloseAsync` を持たせる
- 結果を返さない画面から戻ったことを知りたいだけなら、Push 元の `WillPopEnterAsync` を使う
- 表示だけ・操作を Push 元に渡すだけの Modal にも、Push の型として薄い Presenter を用意する
- Overlay(Modal より手前のレイヤー)はコンテナだけ用意してあり、使う画面はまだ無い。通信エラーのダイアログは Overlay ではなく Bootstrap シーンに置く
  ([通信エラーのダイアログ](error-dialog.md))

## 遷移の直列化

- フォークの `ScreenNavigator` はコンテナごとに遷移を順番待ちにする。USN は遷移中の Push/Pop を例外で拒否するため、別々の場所から来た遷移
  (交代選択を閉じている途中に結果の Modal を出す等)が重なっても失敗させない
- ユーザーの連打は対象外(直列化すると押した回数だけ画面が開くため)。遷移中は USN の設定で全コンテナの入力を止め、遷移を始める前
  (通信を待つ間など)は Presenter で `AwaitOperation.Drop` や `Take(1)` を使う

## シーンの切り替え

- `ISceneNavigator` にはアプリ側の `TransitionAwareSceneNavigator` を登録する。全コンテナの遷移が終わるのを待ってから、フォークの
  `SceneNavigator` に委ねる。USN の遷移アニメーションは `DontDestroyOnLoad` のオブジェクトで動くため、遷移中にシーンごと画面を破棄すると例外になるため
- 切り替え中は前後のシーンが同時に読み込まれている。USN のコンテナは名前を静的な辞書に登録するため、Home/Battle のコンテナの名前は空にしておく
