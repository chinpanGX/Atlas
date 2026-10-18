---
title: "APIサーバーの構成"
status: current
area: server
related: [server/auth, server/internal-api, conventions/errors, conventions/player-diff, db/index]
proto: [Shared/proto/atlas/v1, Shared/proto/atlas/internal/v1/battle.proto]
code: [Server/src/main.rs, Server/src/grpc.rs, Server/src/config.rs, Server/src/state.rs, Server/src/error.rs, Server/src/db.rs, Server/build.rs, Server/config/server_status.toml]
updated: 2026-10-19
---

# APIサーバーの構成

## プロセスとポート

クライアント向けの API と、バトルサーバー向けの内部 API を、同じプロセスの別のポートで出す。どちらも gRPC(tonic)。

| 待ち受け | 環境変数 | 既定 | 中身 |
|---|---|---|---|
| クライアント向け | `GRPC_ADDR` | `127.0.0.1:50052` | 基盤の `DeviceService` と、Atlas の `PlayerService`・`PartyService`・`ScoutService`・`ShopService`・`MatchmakingService`・`DebugService` |
| 内部 API | `SERVER_ADDR` | `127.0.0.1:3000` | `atlas.internal.v1.BattleInternalService`([内部API](internal-api.md)) |

- 同じプロセスにするのは、マッチングの待機列(メモリ)をクライアント向けと内部 API で共有するため
- ポートを分けるのは、基盤の Layer(認証・冪等性・メンテナンス・日付変更)を内部 API に付けないため。メンテナンス中でも
  バトルサーバーからの結果の報告を受ける

## 起動の順番(`main.rs`)

1. `.env` を読み、ログの設定(`LOG_FORMAT`・`RUST_LOG` など。[開発環境ガイド](../development.md))でログを準備する(基盤の `voltech_grpc_log::init`)
2. `config/server_status.toml`(`SERVER_STATUS_PATH` で変更可)を読む。一度も読めなければ起動しない。以後5秒ごとに読み直す
3. MySQL に接続し、マイグレーションを流す(`db::migrate`。基盤の `voltech_grpc_auth`・`voltech_grpc_transaction` → Atlas の `migrations/`)
4. `AppState` を作る。マスターデータを DB から全件読み込み、`BATTLE_TOKEN_SECRET`・`INTERNAL_API_SECRET`(どちらも無ければ起動しない)・
   `BATTLE_SERVER_URL` を読む
5. [結果報告が届かない対戦の後始末](stale-match-cleanup.md) の定期処理と、待ち時間を過ぎた人をボットと組ませる定期処理([マッチング](matchmaking.md))を起動する
6. 2つのポートで待ち受ける

`config/server_status.toml` はメンテナンス・最低バージョン・ゲーム内の日付が変わる時刻を持つ(基盤の `ServerStatus`)。
再起動せずにメンテナンスを切り替えられる。

## Layer の順番(クライアント向け)

```
RpcLogLayer                   RPC ごとの span とアクセスログ(メンテナンス・認証のエラーも出すように一番外側)
└ ServerStatusLayer           強制アップデートとメンテナンス(認証より先に全サービスで判定)
  └ RpcMethodLayer            RPC の名前を extensions に入れる(Transactions::run が読む)
    ├ DeviceService           デバイス登録・トークン発行(認証なし)
    └ AuthLayer               トークンの検証(Atlas の各サービスにだけ付ける)
      └ DateChangeLayer       ゲーム内の日付の変更(認証の後に判定する)
        └ Player/Party/Scout/Shop/Matchmaking/DebugService
```

- 開発ビルドでは gRPC リフレクションを有効にし、grpcurl・Postman から `.proto` 無しで呼べる
- 内部 API には `RpcLogLayer` だけを付ける

## ログ

ログの出力の準備と、RPC ごとの span・アクセスログは基盤(`voltech-grpc-log`)が行う。Atlas は環境変数から設定を読んで渡すだけ(`config.rs`)。

- 標準出力に1行1件の JSON で出す(`LOG_FORMAT=text` で人が読む形)。どの行にもサービス名 `atlas-api` と環境名(`APP_ENV`)が付く
- RPC の span には `request_id`(`x-request-id`)・`rpc.method`・`player_id`(`AuthLayer` が書く)が入り、その中で出たログにも付く。
  RPC の終わりに、ステータス・業務エラーの reason・処理時間のアクセスログが1行出る
- 内部 API はプレイヤーをリクエストの本文で受け取るので、`GetParty`・`GetLoadouts` はハンドラが span に `player_id` を書く。
  `x-internal-secret` が合わなかったときは warn を出す
- 内部エラーは、原因(DB のエラーなど)をたどった連なりと発生した場所を error で出す。クライアント・バトルサーバーには詳細を返さない
- 業務の出来事を info で出す: マッチ成立(`battle match formed`)、ボットとのマッチ成立(`battle match with bot formed`)、ボットの作成(`bot player created`)、
  対戦結果の記録(`battle result recorded`)、古い対戦の打ち切り。ボットとのマッチングと古い対戦の打ち切りは、失敗したときに原因を error で出す

## 使っているもの

ライブラリと用途は [利用技術](../tech-stack.md)。

- クライアント向けの gRPC には VolTech gRPC のクレート(認証・冪等性・トランザクション・メンテナンスの判定)を Layer として付ける。
  内部 API には付けず、共有シークレットで認証する
- 設定は `.env`(DB の接続先・共有シークレット等)から読む
- テストは `cargo test`。DB を使うテストは `sqlx::test`(テストごとに DB を作る)、API の結合テストはテストごとに gRPC サーバーを起動して
  tonic のクライアントで呼ぶ

## ディレクトリ

`grpc`(ハンドラ)・`service`(業務処理と DB アクセス)・`model`(テーブルに対応する構造体)の3層に分ける。

| 場所 | 役割 |
|---|---|
| `grpc/` | gRPC のサービスの実装(ハンドラ)。業務処理は `service/` を呼ぶ |
| `grpc.rs` | サーバーの組み立て(Layer の順番・リフレクション・内部 API のサーバー) |
| `grpc/context.rs` | 認証の情報(`AuthContext`)からプレイヤー ID を取り出す。未作成なら `PLAYER_NOT_FOUND` |
| `grpc/player_lookup.rs` | 基盤の `PlayerResolver` の実装(`players` を引いて作成済みかを返す) |
| `grpc/player_data.rs` | DB の行 → `.proto` の型(`PlayerItem` 等)への変換 |
| `grpc/internal_battle.rs` | 内部 API(`BattleInternalService`)とシークレットの interceptor |
| `proto.rs` | `build.rs` が `Shared/proto`・`Shared/errors.toml` から生成したコード(コミットしない) |
| `service/` | 業務処理と DB アクセス。ハンドラから基盤のトランザクションを受け取って呼ぶ |
| `service/player_diff.rs` | `PlayerDiffCollector`([所持データの差分](../conventions/player-diff.md)) |
| `model/` | DB のテーブルに対応する構造体 |
| `master/` | マスターデータの型(生成物)と、起動時に読み込むキャッシュ(`MasterData`) |
| `error.rs` | `AppError`(サービス層のエラー) |
| `config.rs` | ログの設定とゲームログの出力先を環境変数から読む |
| `db.rs` | 起動時のマイグレーション |
| `state.rs` | `AppState`(DB 接続プール・マスタのキャッシュ・マッチングの待機列・対戦の設定) |
| `bin/` | `seed_master_data`(マスターデータの DB への投入) |

## トランザクションと冪等性

- クライアント向けのハンドラは、業務処理を基盤の `Transactions::run` の中で動かす(1リクエスト1トランザクション)
- 冪等性の対象の RPC(`.proto` で `idempotency_level` を書かないもの)は、同じ RequestID の再送で二重に実行しない。
  プレイヤーごとに直前のレスポンスを1件だけ保存する(基盤のテーブル `last_responses`)
- 読むだけの RPC は `NO_SIDE_EFFECTS`、何回実行しても同じ結果になる RPC は `IDEMPOTENT` にする。対象かどうかは起動時に
  `.proto` の `idempotency_level` から決める(`IdempotencyLevels`)
- 内部 API は基盤のトランザクションを通らない。`ReportResult` は自分でトランザクションを張り、ゲームログを書いてコミットする([ゲームログ](game-log.md))

## エラー

- 業務エラーは `FAILED_PRECONDITION` + `ErrorInfo`([業務エラー](../conventions/errors.md))
- サービス層は `AppError`(`BadRequest`・`NotFound`・`Conflict`・`InternalError`)か、機能ごとのエラー型(`PartyError`・`ScoutError`・`ShopError`)を返し、
  ハンドラが `tonic::Status` に変える。クライアント向けでは機能ごとのエラーを業務エラーに、`AppError` を `INTERNAL` にする。
  内部 API では `AppError` を `INVALID_ARGUMENT`・`NOT_FOUND`・`ALREADY_EXISTS`・`INTERNAL` にする
- 内部エラー(`AppError::InternalError`・`PartyError::Internal`・`ScoutError::Internal`・`ShopError::Internal`)は基盤の `Internal` を持つ。DB などのエラーからは `?` で作れ、
  原因と `?` を書いた場所を持ったまま運ばれる。ハンドラは `internal(err)`(または `or_internal()`)で `INTERNAL` にし、そのときに原因がログに出る

## API 一覧

各 RPC の仕様は `.proto` のコメントが正。

| サービス / RPC | 内容 | 冪等性 | ページ |
|---|---|---|---|
| `voltech.v1.DeviceService/Register` | デバイスの登録(基盤) | `IDEMPOTENT` | [認証](auth.md) |
| `voltech.v1.DeviceService/IssueToken` | アクセストークンの発行(基盤) | `IDEMPOTENT` | [認証](auth.md) |
| `atlas.v1.PlayerService/SignUp` | プレイヤーの作成(初期データの付与) | `IDEMPOTENT` | [プレイヤー](player.md) |
| `atlas.v1.PlayerService/SignIn` | 所持データの全件取得 | `IDEMPOTENT` | [プレイヤー](player.md) |
| `atlas.v1.PartyService/SetParty` | パーティの編成(全置き換え) | 対象 | [パーティ編成](party.md) |
| `atlas.v1.PartyService/SetPachimonMove` | 技の付け替え(育成チケット5枚) | 対象 | [パーティ編成](party.md) |
| `atlas.v1.PartyService/SetPachimonStatPoints` | ステータスポイントの割り振り(増やした分の育成チケット) | 対象 | [パーティ編成](party.md) |
| `atlas.v1.ScoutService/ListBanners` | 開催中のバナー一覧 | `NO_SIDE_EFFECTS` | [スカウト](scout.md) |
| `atlas.v1.ScoutService/Roll` | 紹介を受ける(ジェムを消費して候補10体) | 対象 | [スカウト](scout.md) |
| `atlas.v1.ScoutService/Select` | 候補から1体を選んで入手 | 対象 | [スカウト](scout.md) |
| `atlas.v1.ShopService/ListProducts` | 商品の一覧 | `NO_SIDE_EFFECTS` | [ショップ](shop.md) |
| `atlas.v1.ShopService/Purchase` | 商品を1つ購入(ジェムを払ってアイテムを受け取る) | 対象 | [ショップ](shop.md) |
| `atlas.v1.MatchmakingService/JoinQueue` | 待機列に入る(成立すれば対戦を記録) | 対象 | [マッチング](matchmaking.md) |
| `atlas.v1.MatchmakingService/LeaveQueue` | 待機列から抜ける | `IDEMPOTENT` | [マッチング](matchmaking.md) |
| `atlas.v1.MatchmakingService/GetQueueStatus` | 成立したかと接続情報 | `NO_SIDE_EFFECTS` | [マッチング](matchmaking.md) |
| `atlas.v1.DebugService/GrantGems` | 開発用のジェム付与 | 対象 | [開発用API](debug-api.md) |
| `atlas.v1.DebugService/RandomizeParty` | 開発用のランダム編成 | 対象 | [開発用API](debug-api.md) |
| `atlas.internal.v1.BattleInternalService/GetParty` | 参加者のパーティ取得 | `NO_SIDE_EFFECTS` | [内部API](internal-api.md) |
| `atlas.internal.v1.BattleInternalService/GetLoadouts` | 選出個体の所持データ取得 | `NO_SIDE_EFFECTS` | [内部API](internal-api.md) |
| `atlas.internal.v1.BattleInternalService/ReportResult` | 対戦結果の記録 | (基盤を通らない) | [内部API](internal-api.md) |

## API を足すとき

1. `Shared/proto/atlas/v1/` に RPC とメッセージを書く(新しいサービスなら `build.rs` の `PROTO_FILES` にも足す)
2. 業務エラーが要れば `Shared/errors.toml` に足す
3. `grpc/` にハンドラ、`service/` に業務処理を書き、`grpc.rs` で `AuthLayer`・`DateChangeLayer` を付けて登録する
4. ルートで `make proto-gen`・`make errors-gen` を実行し、Unity 向けの C# を生成し直す
