---
title: "サインイン"
status: current
area: client
related: [server/player, server/auth, client/services-and-player-data, client/error-dialog]
code: [Client/AtlasUnityProject/Assets/Scripts/Infrastructure/SignInService.cs, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/PlayerSignInService.cs, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/PlayerSession.cs, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/FileAccountStore.cs, Client/AtlasUnityProject/Assets/Scripts/Presentation/Title]
updated: 2026-10-11
---

# サインイン

起動時の初期化ではマスタだけを読み込み、Home へ進む。Home シーンは、サインインしていなければ(`IPlayerSession.IsSignedIn`)
タイトルから始まる。タイトルの「スタート」でサインインする。

```
TitlePresenter(スタート)
  └─ ISignInService.SignInAsync()
       ├─ IPlayerSignInService.SignInAsync     PlayerService/SignIn(最初の通信で ApiCore がデバイス登録・トークンの発行を行う)
       │    └─ PlayerSnapshotApplier            → 各 Repository(置き換え)
       ├─ (PLAYER_NOT_FOUND なら)SignUpAsync("プレイヤー") → もう一度 SignIn
       ├─ IPlayerProfileRepository              プレイヤー ID・ニックネーム
       └─ IPlayerSession.MarkSignedIn()
  → HomePresenter を Push
```

- サインインを起動処理から外したのは、通信エラーのダイアログや「タイトルへ」の遷移を基盤が出すため。起動処理の中ではタイトルへ戻る先が無い
- サインアップ済みかは端末に覚えず、毎回サインインの結果で判断する
- ニックネームの入力画面は未実装のため、サインアップでは固定値「プレイヤー」を使う
- サインインの `PLAYER_NOT_FOUND` は、ダイアログを出さずにサインアップへ進むため、ラッパーの `handledCodes` で自分で受け取る
- 通信中の連打は捨てる。失敗したら再び押せる
- `IPlayerSession` を `ISignInService` と分けているのは、エラーの後の画面遷移(`ErrorNavigator`)が `ApiCore` から呼ばれるため。
  サインイン(通信)に依存すると、`ApiCore` → `ErrorNavigator` → `ISignInService` → `ApiCore` と循環する

## アカウント情報の保存

デバイス登録で受け取ったアカウント情報(`device_id`・`secret_key`・`player_id`)は、基盤の `IAccountStore` を Atlas が実装した
`FileAccountStore` が、Supplement の暗号化セーブでファイルに保存する。基盤の既定はメモリだけで、起動するたびに新しい端末になるため。

- 接続先(gRPC のアドレス)ごとにファイルを分ける。接続先を切り替えると、別の環境の DB には保存した `device_id` が無く、トークンを発行できなくなるため
- 所持データはディスクに保存しない([サービスと所持データ](services-and-player-data.md))
- 同じ PC で2人分を動かすときの保存先の分け方は [シーンとスコープ](scenes-and-scopes.md)

## 接続先

APIサーバーのアドレス(`http://127.0.0.1:50051`)とアプリのバージョン(サーバーが強制アップデートの判定に使う整数)は、
`RootLifetimeScope` の定数で持つ。環境(本番・ステージング等)を切り替える仕組みは未導入。
