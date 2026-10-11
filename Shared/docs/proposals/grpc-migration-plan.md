# gRPC 基盤(VolTech gRPC v0.1.0)への乗せ換えの計画

APIサーバー(Rust/axum)の REST の API と、Unity クライアントの API 通信層を、VolTech gRPC(以下「基盤」)に乗せ換える計画。
指示は [feature-grpc-migration.md](feature-grpc-migration.md)。確認をもらってから実装を始める。

- 作業ブランチ: `feature/grpc-migration`(`develop` の `51ff6da5` から)。REST 版は git の履歴(`51ff6da5`)で見られる
- 基盤は `C:\Work\VolTech-gRPC` の `v0.1.0`(`db2068a`)を読んで調べた

## 0. 先に決めてほしいこと

| # | 決めること | 提案 |
|---|---|---|
| A | BattleBot(`BattleServer/BattleBot/`)の扱い | **乗せ換える**。BattleBot は APIサーバーの REST(`/devices`・`/signup`・`/sign-in`・`/debug/randomize_party`・`/battle/queue*`)を呼ぶクライアントなので、REST を消すと動かなくなる。基盤には Unity 以外の .NET のクライアントが無いので、`Grpc.Tools` で生成したクライアントを素のまま使い、ヘッダー(`authorization`・`x-request-id`)を自分で付ける(9章の「基盤に足りないもの」#3) |
| B | 移す途中の認証の橋渡し | **作らない**。REST の実装は捨ててよいことになったので、REST と gRPC を並べて動かす期間を作らない(4章) |
| C | `/debug/*` | **gRPC に移す**(`DebugService`)。`player_diff` の反映がほかの API と同じ仕組みで動き、BattleBot も同じ経路で呼べる。環境変数 `ENABLE_DEBUG_API` が立っているときだけ登録する |
| D | チャット(`/chat/send`・`/chat/poll`) | Unity クライアントには呼び出し元が無い(生成された `ChatApiClient` だけ)。**→ 移さずに機能ごと消すことにした**(使う画面が無いため。`messages` テーブルは新しいマイグレーションで消す) |
| E | `GET /battle/queue/status` の、マッチ情報を一度返したら消す動き | **消さないように変える**(下の 6章)。再送や二重の呼び出しでマッチ情報を失わず、読むだけの RPC(`NO_SIDE_EFFECTS`)にできる |
| F | サインインを起動処理から外す | エラーダイアログ・タイトルへ戻る遷移を基盤が出すので、サインインを **Home シーンの最初のタイトル画面** に移す(7章)。今の起動処理(Bootstrap)でのサインインは、ダイアログを出す画面が無い |
| G | `.proto`・`errors.toml` の置き場所 | `Shared/proto/atlas/v1/*.proto`、`Shared/errors.toml`(domain `atlas`)、生成の `Makefile`・`buf.yaml`・`buf.gen.yaml` はリポジトリ直下 |

## 1. 依存の版と、サーバーの構成

### sqlx を上げる範囲

- 基盤のクレートは `sqlx::MySqlPool`・`sqlx::Transaction` を公開の型に使う(`AuthLayer::new(pool, ..)`・`Transactions::run` の `tx`・`migrate(&pool)`)。sqlx 0.8 と 0.9 が混ざると型が合わないので、**APIサーバー全体を sqlx 0.9 に上げる**(`default-features = false`、`runtime-tokio`・`mysql`・`mysql-rsa`・`migrate`・`macros`・`chrono`・`json`)
- 合わせて上げるもの: `ulid` 1 → 3(基盤の `PlayerId` が `ulid::Ulid` 3。`Ulid::new()` は `Ulid::generate()` 等に書き換え)、`rand` 0.8 → 0.10(基盤と同じ)、`tower-http` 0.5 → 0.7(`TraceLayer::new_for_grpc`)
- 足すもの: `tonic`・`tonic-prost`・`prost`・`prost-types`・`tonic-types`(0.14)、`tonic-reflection`(開発時のリフレクション)、`voltech-grpc-{auth,error,status,transaction}`(git + `tag = "v0.1.0"`)、ビルド用に `protox`・`tonic-prost-build`・`voltech-grpc-error-build`
- 消すもの(最後のまとまりで): `utoipa`・`utoipa-swagger-ui`、Atlas の認証に使っていた `argon2`(基盤が持つ)
- sqlx-cli も 0.9 にそろえる(`make migrate` を残す場合)
- **最初のまとまり(準備)で、REST のまま sqlx 0.9 等に上げてテストを通してから**、gRPC を足す。版上げの不具合と乗せ換えの不具合を混ぜないため

### 内部API(axum)と gRPC を同じプロセス・同じポートで出すか

**同じプロセスで、ポートは分ける**(gRPC は `GRPC_ADDR`、既定 `127.0.0.1:50051`。内部APIは今の `SERVER_ADDR` `127.0.0.1:3000` のまま)。

- 同じプロセスにするのは、マッチングの待機列(`AppState.matchmaking`)をメモリで共有しているため(サーバー1台の前提)。`AppState` を tonic のサービスと axum の両方に渡す
- ポートを分けるのは、
  - 基盤の Layer(`ServerStatusLayer`・`RpcMethodLayer`・`AuthLayer`・`DateChangeLayer`)は tonic の `Server::builder()` に付ける前提で、内部APIには付けたくない(メンテナンス中でも BattleServer からの結果報告は受けたい)
  - BattleServer の内部APIの呼び出し(`HttpClient`、HTTP/1.1)を変えずに済む。同じポートで HTTP/1.1 と h2c を出し分けるより単純
- axum は 0.7 のまま残す(残るのは `/internal/battle/*` の3つだけ)。`tonic` 0.14 の `Routes::into_axum_router` で同じポートにまとめる案は、上の理由で採らない

## 2. テーブル

### スキーマの違い

| | Atlas(今) | 基盤 |
|---|---|---|
| `devices` | `device_id CHAR(26)` PK、`secret_key_hash VARCHAR(255)`、`created_at` | `device_id CHAR(26) ascii_bin` PK、`player_id BINARY(16)`、`secret_hash VARCHAR(255)`、`created_at` |
| `access_tokens` | `device_id` PK、`access_token VARCHAR(64)` UNIQUE、`expires_at`、`created_at` | `device_id` PK、`token CHAR(64)` UNIQUE、`expires_at` |
| `players` | `player_id CHAR(26)` PK、**`device_id`**(FK → devices)、`nickname`、`created_at` | (アプリが持つ。`device_id` は持たない) |
| `last_responses` | 無い | `player_id BINARY(16)` PK、`request_id`・`method`・`response` |
| マイグレーションの履歴 | `_sqlx_migrations` | `_auth_migrations`・`_transaction_migrations` |

- 基盤のテーブルの名前は変えられず、基盤の `migrate` は `CREATE TABLE devices` から始まる。Atlas の `devices`・`access_tokens` があると失敗する
- `player_id` は基盤の側だけ `BINARY(16)`。Atlas のテーブル(`players` ほか)は `CHAR(26)` のまま変えない。つなぎ目(`PlayerResolver` と、ハンドラーで `AuthContext.player_id` を使うところ)で `Ulid::to_string()` / `Ulid::from_string()` で変える

### マイグレーション(既存のデータは捨てる)

DB のデータは捨ててよいので、引き継ぎはしない。DB は作り直す(`server-dev-env` の DB のリセット)。

- **Atlas のマイグレーションを書き換える**: `devices`・`access_tokens` を作るマイグレーションを消し、`players` から `device_id` を外す(作り直す前提なので、適用済みのマイグレーションを書き換えてよい)。Atlas の履歴に、基盤とぶつかるテーブルが一度も出てこないようにする
- 起動時に、基盤のマイグレーション(`voltech_grpc_auth::migrate`・`voltech_grpc_transaction::migrate`)→ Atlas のマイグレーション(`sqlx::migrate!()`)の順に流す。今は `make migrate` で sqlx-cli から流しているが、基盤のマイグレーションはクレートに埋め込まれていて sqlx-cli から流せないので、サーバーの起動で流す形にそろえる
- `last_responses` の行は、サインアップで `create_last_response` を呼んで作る
- テスト(`#[sqlx::test]`)は既定で `./migrations` だけを流すので、`#[sqlx::test(migrations = false)]` にして、基盤と Atlas のマイグレーションを流すテスト用の関数を通す

## 3. 認証

| Atlas(今) | 基盤 |
|---|---|
| `POST /devices`(`secretKey` → `deviceId`) | `voltech.v1.DeviceService/Register`(`player_id` も発行する) |
| `POST /devices/authenticate`(→ `accessToken`・`expiresIn`) | `DeviceService/IssueToken`(→ `access_token`・`expires_at`) |
| `GET /auth/verify` | **無くす**(トークンの確認は `ApiCore` が期限で行い、弾かれたら発行し直す) |
| `POST /signup`(`nickname`、作成済みなら 409) | `atlas.v1.PlayerService/SignUp`(`IDEMPOTENT`。**作成済みならエラーにせず作成済みのプレイヤーを返す**) |
| `POST /sign-in`(未作成なら 404) | `PlayerService/SignIn`(`IDEMPOTENT`。未作成なら `PLAYER_NOT_FOUND`) |
| `AccessTokenRefresher`(期限前の再認証・401 で1回だけ再送・single-flight) | `ApiCore` + `DeviceTokenProvider`(同じことを基盤が行う)。**消す** |
| `AccessTokenStore`・`DeviceConnection`・`IDeviceConnection`・`DeviceCredentials`・`IDeviceCredentialsRepository` | **消す**。保存は `IAccountStore` の実装(7章) |
| サーバーの `AuthenticatedDevice`(トークン → `device_id` → `players` を `device_id` で引く) | `AuthLayer` + `PlayerResolver`(`AuthContext.player_id` → `players`)。`find_by_device_id` は使わなくなる |

- **「プレイヤー未作成」**: 基盤の決まりどおり、業務エラー `PLAYER_NOT_FOUND` で返す。`AuthContext.player_created` が false のとき、作成済みのプレイヤーが要るハンドラーはすべてこれを返す(sandbox の `api::context::player_id` と同じ関数を Atlas に置く)。クライアントはサインインだけ `handledCodes` に `AtlasErrors.PlayerNotFound` を渡し、サインアップ → サインインへ進む。それ以外の API で返ったら既定の action(`title`)でタイトルへ戻る
- `PlayerResolver` は `players` を `player_id` で引き、無ければ `NotCreated`、あれば `Active`。Atlas に BAN は無いので `Suspended` は返さない
- サインアップは `AuthContext.player_id`(デバイス登録で発行済み)でプレイヤーを作り、同じトランザクションで `create_last_response` を呼ぶ。`players.device_id` は2章のマイグレーションの書き換えで無くなる

## 4. REST の実装の扱い(決めてほしいこと B)

REST の API(`/internal/battle/*` を除く)とクライアントの REST の通信層は、すべて捨ててよい(REST 版は git の履歴の `51ff6da5` で見られる)。

- REST と gRPC を並べて動かす期間を作らない。移す途中の認証の橋渡し(REST が基盤のトークンを読む等)は作らない
- 最初のまとまり(認証とサインイン)で、REST の公開 API・`AccessTokenRefresher`・生成した REST のクライアントをまとめて消す。まだ移していない機能(編成・スカウト等)は、そのまとまりを移すまで動かない
- 内部API(`/internal/battle/*`)は指示どおり REST のまま残す

## 5. エラー(`AppError` → `errors.toml` の業務エラー)

`Shared/errors.toml`(domain `atlas`)に書く。予約エラー(`voltech`)は書かない。

| 今 | どこで | 業務エラー(reason) | action |
|---|---|---|---|
| 404 プレイヤー未作成 | サインイン・ほか全API | `PLAYER_NOT_FOUND` | title |
| 409 作成済み | サインアップ | (無くす。作成済みのプレイヤーを返す) | |
| 400 人数が1〜6体でない・slot が範囲外・slot / 個体の重複 | 編成 | `INVALID_PARTY` | stay |
| 404 自分の所持個体でない | 編成・技の付け替え | `PACHIMON_NOT_OWNED` | stay |
| 400 slot が1〜4でない | 技の付け替え | `INVALID_MOVE_SLOT` | stay |
| 400 候補技でない | 技の付け替え | `MOVE_NOT_LEARNABLE` | stay |
| 404 バナーが無い | スカウトの紹介 | `SCOUT_BANNER_NOT_FOUND` | stay |
| 400 開催期間外 | スカウトの紹介 | `SCOUT_BANNER_CLOSED` | stay |
| 400 ジェム不足(`insufficient items`) | スカウトの紹介 | `INSUFFICIENT_GEMS` | stay |
| 404 ロールが無い・自分のものでない | スカウトの選択 | `SCOUT_ROLL_NOT_FOUND` | stay |
| 400 index が範囲外 | スカウトの選択 | `SCOUT_CANDIDATE_OUT_OF_RANGE` | stay |
| 409 選択済み | スカウトの選択 | `SCOUT_ROLL_ALREADY_SELECTED` | stay |
| 400 パーティが空 | マッチングの参加 | `PARTY_EMPTY` | stay |
| 401 | 全API | (基盤の `UNAUTHENTICATED`。`AuthLayer` が返す) | |
| 500 | 全API | (基盤の `internal()` → `INTERNAL_ERROR`) | |

- 今の `BadRequest` は「ジェム不足」と「開催期間外」が同じ 400 だった。画面で出し分けられるように reason を分ける
- `AppError` は内部API(`/internal/battle/*`)のためだけに残す。gRPC のハンドラーは `tonic::Status`(`failed_precondition`・`internal`)を返す。サービス層は機能ごとのエラーの enum(例: `ScoutError::InsufficientGems`)を返し、ハンドラーで `Status` に変える(sandbox の `DrawError` と同じ形)
- 文言キー(`error.atlas.insufficient_gems` 等)の文言は、エラーダイアログが引く表に足す(7章)

## 6. `.proto` の案

パッケージ `atlas.v1`、C# の名前空間 `Atlas.Api.V1`。共通レスポンス項目は `player_diff` だけを使う(バッジ・通知は今の Atlas に無いので足さない)。

| サービス | RPC | 今の REST | 冪等性 | 返す `player_diff` |
|---|---|---|---|---|
| `PlayerService` | `SignUp(nickname)` | `POST /signup` | `IDEMPOTENT`(作成済みなら作成済みを返す) | なし |
| | `SignIn()` | `POST /sign-in` | `IDEMPOTENT` | なし(全件は別フィールド。下記) |
| `PartyService` | `SetParty(party_slots)` | `POST /edit/party` | 対象(書かない)。編成し直すたびに割当の ULID を作り直すので、2回実行すると結果が変わる | `party_slots` |
| | `SetPachimonMove(player_pachimon_id, slot, move_id)` | `POST /edit/pachimon_moves` | 対象 | `pachimon_move_map` |
| `ScoutService` | `ListBanners()` | `GET /scout/banners` | `NO_SIDE_EFFECTS` | なし |
| | `Roll(banner_id)` | `POST /scout/rolls` | 対象(ジェムを消費する) | `items` |
| | `Select(roll_id, index)` | `POST /scout/rolls/{rollId}/select` | 対象 | `pachimon`・`pachimon_move_map` |
| `MatchmakingService` | `JoinQueue()` | `POST /battle/queue` | 対象(成立したら `battle_matches` に書く) | なし |
| | `LeaveQueue()` | `DELETE /battle/queue` | `IDEMPOTENT` | なし |
| | `GetQueueStatus()` | `GET /battle/queue/status` | `NO_SIDE_EFFECTS`(決めてほしいこと E) | なし |
| `ChatService` | `SendMessage(content)` | `POST /chat/send` | 対象 | なし |
| | `ListMessages()` | `GET /chat/poll` | `NO_SIDE_EFFECTS` | なし |
| `DebugService` | `GrantGems()` | `POST /debug/grant_gems` | 対象 | `items` |
| | `RandomizeParty()` | `POST /debug/randomize_party` | 対象 | `pachimon`・`pachimon_move_map`・`party_slots` |

```proto
// 形の案(フィールドのコメントは実装のときに書く)
message PlayerDiff {
  ItemsDiff items = 1;                    // null = 変わっていない
  PachimonDiff pachimon = 2;
  PachimonMoveMapDiff pachimon_move_map = 3;
  PartySlotsDiff party_slots = 4;
}
message ItemsDiff { repeated PlayerItem upserted = 1; repeated int32 removed = 2; }
message PachimonDiff { repeated PlayerPachimon upserted = 1; repeated string removed = 2; }
// PachimonMoveMapDiff・PartySlotsDiff も同じ形

message SignInResponse {
  string player_id = 1;
  string nickname = 2;
  PlayerSnapshot snapshot = 3;   // 全件。player_diff と名前を分け、ApiCore に自動で反映させない
}
message PlayerSnapshot {
  repeated PlayerItem items = 1;
  repeated PlayerPachimon pachimon = 2;
  repeated PlayerPachimonMove pachimon_moves = 3;
  repeated PartySlot party_slots = 4;
}

message GetQueueStatusResponse {
  QueueStatus status = 1;        // QUEUE_STATUS_WAITING / QUEUE_STATUS_MATCHED(今の文字列を enum に)
  string match_id = 2;           // 成立していなければ空
  string battle_server = 3;
  string battle_token = 4;
}
message ScoutBanner {
  string banner_id = 1;
  string name = 2;
  int32 cost_per_roll = 3;
  google.protobuf.Timestamp start_at = 4;
  google.protobuf.Timestamp end_at = 5;
}
```

- **サインインの全件**は `player_diff` に入れず、`snapshot` で返す。今の Atlas はサインインの全件を `playerDiff` として Upsert していたが、タイトルに戻ってサインインし直すと、サーバーで無くなったものが手元に残る。基盤の決まりどおり、呼び出し側(`SignInService`)が各 Repository を空にしてから入れる(置き換え)
- 今の `PlayerDiffDto` は、変わらないリソースも空の `upserted`/`removed` で必ず入っていた。`.proto` ではリソースごとのメッセージを null にして「変わっていない」を表す(基盤の規約。クライアントの反映処理は null のリソースを飛ばす)
- **`GetQueueStatus` の変更(E)**: 今の `take_match` は、マッチ情報を一度返したら待機列から消す。応答が届かずに送り直すと、マッチ情報を失う。返しても消さず、次の `JoinQueue`・`LeaveQueue`、または対戦の結果報告で消すように変える。読むだけになるので `NO_SIDE_EFFECTS` にでき、ポーリングがクライアントの順番待ちに並ばない
- `JoinQueue` は、待機列(メモリ)を書き換えた後でトランザクションが失敗すると、メモリだけが残る。今も同じ(待機列への追加は二重にならない作り)なので、そのままにする
- 冪等性の対象の RPC はすべて `Transactions::run` の中で動かすので、サービス層の関数は `&MySqlPool` ではなく、渡されたトランザクション(`&mut Transaction<'_, MySql>`)で読み書きする形に変える。今サービスの中で `pool.begin()` している関数(`set_party`・`randomize_party`・`create_roll`・`select_candidate`・`player_service::create`)は、自分でトランザクションを始めなくなる
- 差分は、sandbox と同じく **アプリ側のコレクター**(`PlayerDiffCollector`)に、書き込む関数が変更後の値を記録し、ハンドラーが最後に `player_diff` に詰める。基盤はコレクターを持たない(当面アプリ側に置く決まり)

## 7. クライアント

### 層の形

```
Presenter → IXxxService(Application。実装は Infrastructure)
              → IXxxConnection(Application。Domain の型で受け渡す)
                  → XxxConnection(Infrastructure.Grpc。生成したラッパー XxxApi を呼び、生成の型を Domain の型に詰め替える)
                      → XxxApi(生成) → ApiCore(基盤) → CallInvoker
```

- `IXxxConnection` は残す。生成したメッセージ型(`Atlas.Api.V1`)は Infrastructure の外に出さない(今の生成 DTO と同じ扱い)
- 戻り値は成否(`bool`)か、Application の結果の型(`SignInResult` など)にする。`Atlas.Application` は基盤を参照しない。失敗のときはダイアログ・遷移を `ApiCore` が済ませているので、Presenter は成否を見て抜けるだけになる。今の Presenter の `try/catch (Exception)` は、その機能を移すときに消す(G-1 で決めた)
- サインインしているかと、タイトルに戻るときに所持データを捨てる処理は `IPlayerSession` に分ける。`ErrorNavigator` が `ISignInService` に依存すると、`ApiCore` → `ErrorNavigator` → `ISignInService` → Connection → `ApiCore` と循環するため(G-1 で決めた)
- `playerDiff` の反映: 基盤の `IPlayerDiffApplier`(`ApiCore` が呼ぶ)を Atlas が実装し、`IHasPlayerDiff` のレスポンスの `PlayerDiff` をリソースごとの Applier(今の `ItemDiffApplier` 等を生成の型に合わせて直す)に振り分ける。Connection が自分で反映する今の形は無くなる
- アセンブリ: `Atlas.Infrastructure.Api` を `Atlas.Infrastructure.Grpc` に置き換える。生成コードは `Assets/Scripts/Infrastructure/Grpc/Generated/{Messages,Grpc,Wrappers,Errors}` に出し、asmdef `Atlas.Api.V1` にまとめる(sandbox の `Sandbox.V1` と同じ形)
- `ApiCore`・ラッパー・`CallInvoker`・`GrpcChannel` は `RootLifetimeScope` に1つずつ登録する。接続先は今の `ApiBaseUrl` と同じく定数(`http://127.0.0.1:50051`)

### VContainer への登録

```csharp
var options = new ApiCoreOptions(UnityAppVersion.Create(AppVersion));
var channel = ApiChannel.Create(GrpcAddress);
var callInvoker = ApiChannel.CreateCallInvoker(channel);
var clock = new ServerClock();
builder.RegisterInstance(callInvoker).As<CallInvoker>();
builder.Register<IAccountStore, EncryptedFileAccountStore>(Lifetime.Singleton);   // Atlas の実装(下記)
builder.Register<ITokenProvider>(r => new DeviceTokenProvider(callInvoker, r.Resolve<IAccountStore>(), options), Lifetime.Singleton);
builder.Register(r => new ApiCore(options, r.Resolve<ITokenProvider>(), new ErrorPolicyProvider(AtlasErrors.All),
        new DefaultCommonErrorHandler(r.Resolve<IErrorDialogView>()), r.Resolve<VolTech.Client.IPlayerDiffApplier>(),
        Debug.isDebugBuild ? new UnityApiLogger() : null, clock, r.Resolve<IErrorNavigator>()), Lifetime.Singleton);
builder.Register(r => new PlayerApi(r.Resolve<ApiCore>(), callInvoker), Lifetime.Singleton);
// PartyApi・ScoutApi・MatchmakingApi・DebugApi も同じ
```

- `GrpcChannel` は `RootLifetimeScope` の破棄で `Dispose` し、`ServerClockResetter.Create(clock)` を Build のときに作る

### アカウント情報の保存(`IAccountStore`)

基盤の既定は `InMemoryAccountStore` だけ(起動するたびに新しい端末になる)。端末に保存しないとプレイを続けられないので、**Atlas 側で `IAccountStore` を実装する**(基盤が差し替え口として用意している interface。9章 #1)。

- 新しいファイルに `device_id`・`secret_key`・`player_id` を保存する。保存先は接続先ごとに分ける(基盤の決まり)
- Supplement の暗号化ファイル保存(`IFileStorageService`)を使う。今の `deviceCredentials` のファイルは読まない(端末のデータは捨ててよいので、引き継がない)

### Fake への差し替え

- 今の `MockDeviceConnection`・`MockPlayerConnection`(Battle の PlayMode テストの `TestRootLifetimeScope` が使う)を、`FakeCallInvoker` に登録する偽のサーバー(`FakePlayerServer` 等。状態を持つ)に置き換える。テストでは `CallInvoker` だけを `FakeCallInvoker` に差し替え、`ApiCore`・ラッパー・反映処理は本物を通す
- 基盤の汎用テスト(全 RPC に偽のレスポンスがあるか・全 RPC を反映処理まで通して `CLIENT_ERROR` が起きないか)を EditMode テストに足す
- `MockBattleMatchmaker`・`MockBattleConnection`(対戦をサーバー無しで試すもの)は残す。マッチングの Real は `MatchmakingApi` を使う実装に変える

### エラーダイアログと画面遷移

- **ダイアログ(`IErrorDialogView`)**: 新しい prefab `ErrorDialog` を、Bootstrap シーンに常駐する最前面の Canvas に置く。USN の Modal にしない(USN のコンテナはシーンごとにあり、シーンの切り替え中や Bootstrap ではエラーを出せないため)。見た目は今の確認用の Modal(`ScoutConfirmModal`)にそろえ、色は `PaletteColor`、文字はタイトルと本文と、action ごとのボタン(OK / 再試行・タイトルへ / ホームへ / ストアへ)。prefab は uloop から作る
- 文言は、エラーコード(domain・reason)から決まる文言キーで引く表(`ErrorMessageTable`)を Atlas に置く。予約エラー(メンテナンス・通信エラー等)の文言もここに書く
- **画面遷移(`IErrorNavigator`)**:
  - `GoToTitleAsync`: 各 Repository を空にして、`ISceneNavigator` で Home シーンへ切り替える(Home シーンの最初の画面をタイトルにする。決めてほしいこと F)
  - `GoToHomeAsync`: Home シーンへ切り替え、タイトルを飛ばしてホームを開く(Battle シーンにいるとき用)
  - `OpenStoreAsync`: ストアはまだ無いので、ログを出すだけにする
- **サインインの場所(F)**: 今は Bootstrap の起動処理(`SignInInitializationTask`)でサインインしている。これをやめ、Home シーンの最初の画面をタイトル画面(今は使われていない `TitlePage`)にして、「スタート」で サインイン →(未作成なら)サインアップ → ホームへ進む。タイトルへ戻るエラー(`DATE_CHANGED` 等)からも同じ流れでサインインし直せる

## 8. Unity の依存

| 項目 | Atlas | 基盤のパッケージ | 対応 |
|---|---|---|---|
| Grpc.Net.Client / Grpc.Core.Api | 2.84.0(Unity NuGet。MagicOnion 7.11.0 は 2.57.0 以上を要求) | 2.84.0 | **同じ DLL を共有できる**。MagicOnion は NuGetForUnity の `Assets/Packages` に入っているが、gRPC の DLL は Unity NuGet(`org.nuget.*`)から1つだけ入っている |
| System.IO.Pipelines | 10.0.12 | 10.0.12 | 同じ |
| UniTask | 2.5.11 | 2.5.11 | 同じ |
| YetAnotherHttpHandler | **1.11.5**(manifest で直接指定) | **1.11.6** | manifest に直接書いた版が優先され、基盤が確かめていない 1.11.5 のままになる。**1.11.6 に上げる**(MagicOnion の接続も同じハンドラーを使うので、対戦の通信も確かめる) |
| Google.Protobuf / Google.Api.CommonProtos | 無い | 3.31.1 / 2.17.0 | 基盤の依存として入る。MagicOnion は MessagePack を使うので衝突しない |
| Unity | 6000.6.2f1 | 6000.6.5f1 で確認済み | Unity 6000.6 は別の版の Google.Protobuf を同梱していて、版がずれるとエディタの中だけ業務エラーがすべて不明なエラーになる。**基盤のテスト(`ProtobufVersionTests` ほか)を `testables` に入れて Atlas の 6000.6.2f1 で流して確かめる**。失敗したら回避せずに報告する |
| scopedRegistries | OpenUPM(`com.cysharp` ほか)・Unity NuGet(`org.nuget`) | 同じ2つ | 足すものは無い |

## 9. 基盤に足りないもの・合わないもの(報告)

調べた時点で見つかったもの。回避策は作らない。

| # | 内容 | Atlas への影響 | Atlas での扱い |
|---|---|---|---|
| 1 | 端末に保存する `IAccountStore` の実装が無い(`InMemoryAccountStore` だけ。`EncryptedFileAccountStore` は wiki に設計だけある) | 起動するたびに新しい端末になる | 基盤が差し替え口として用意している interface なので、Atlas で実装する(7章)。基盤に入ったら乗り換えるかを決める |
| 2 | 保存したアカウント情報が使えないとき(トークンの発行が `UNAUTHENTICATED`)に、タイトルへ戻って同じ情報で読み直し、抜け出せない(基盤の未確定・保留 #19) | Atlas は開発中に DB をよくリセットする(`server-dev-env`)。リセットのたびにクライアントが進めなくなる | 基盤で直してほしい。それまでは、DB をリセットしたら端末の保存ファイルを手で消す(手順を DEVELOPMENT.md に書く) |
| 3 | Unity 以外の .NET のクライアントが無い(`ApiCore` は Unity のパッケージだけ) | BattleBot(コンソールアプリ)が基盤のクライアントを使えない | BattleBot は `Grpc.Tools` で生成した素のクライアントを使い、`authorization`・`x-request-id`・`x-app-platform` 等を自分で付ける(決めてほしいこと A)。基盤の共通処理(再認証・順番待ち)は通らない |
| 4 | 基盤のテーブル名(`devices`・`access_tokens`・`last_responses`)が固定で、アプリの既存のテーブルと同じ名前だと `migrate` が失敗する | Atlas の `devices`・`access_tokens` とぶつかる | Atlas のマイグレーションから `devices`・`access_tokens` を消す(2章)。基盤を直す必要は無いが、既存のアプリに組み込むときの注意として wiki にあるとよい |
| 5 | 基盤のテーブルの `player_id` は `BINARY(16)`、Atlas は「ULID は `CHAR(26)`」の決まり | つなぎ目で変換が要る | Atlas のテーブルは変えず、つなぎ目で変換する(2章) |
| 6 | サインインの全件と、`player_diff` の自動の反映の区別が未確定(基盤の #5) | | Atlas はフィールド名を分ける(`snapshot`)形で進める(6章) |
| 7 | 差分のコレクターと、`AuthContext`・`GameDate` を取り出す関数が基盤に無い(sandbox のアプリ側のコード) | | sandbox の形を Atlas に書く(当面アプリ側に置く決まり) |
| 8 | (実装中に発見)基盤のデバイス登録の `.proto`(`voltech/v1/device.proto`)が Rust のクレートの中にしか無く、Unity 以外の .NET のクライアント向けに配られていない | BattleBot が `DeviceService` のクライアントを生成できない | BattleBot のビルドで、基盤のタグの版を `obj/` に取ってきて `Grpc.Tools` に渡す(コピーはしない)。基盤が `.proto` を配るようになったら乗り換える |
| 9 | (実装中に発見)`ApiCore.InvokeAsync`・生成したラッパーが `CancellationToken` を受け取らず、1回の通信を途中で止められない | マッチングのキャンセルが、状況の確認の通信が終わるまで遅れる(最大で1回分) | 状況の確認の合間でキャンセルを見る。基盤で直してほしい |
| 10 | (実装中に発見)fake-testing の wiki にある汎用のテスト(全 RPC に偽のレスポンスがあるか・全 RPC を反映処理まで通して `CLIENT_ERROR` が起きないか)が v0.1.0 のパッケージに無い | 7章の「基盤の汎用テストを EditMode テストに足す」ができない | 基盤に入るまで足さない |

実装の途中で見つかったものは、ここに足して報告する。

## 10. 移す順番

まとまりごとに、サーバー・クライアント・BattleBot を一緒に移し、`cargo test` と Unity の EditMode テストを通してからコミットする(コミットとプッシュは確認をもらう)。移した API の REST とクライアントのコードは、そのまとまりで消す。ドキュメント(CLAUDE.md・DEVELOPMENT.md・design.md・progress.md)の通信の説明・構成図・生成物の表も、そのまとまりで直す。

| # | まとまり | 内容 |
|---|---|---|
| 0 | 準備 | (REST のまま)sqlx 0.9・ulid 3・rand・tower-http を上げてテストを通す → コミット。次に、基盤のクレートと tonic を足し、`DeviceService` と Layer だけを持つ gRPC サーバーを 50051 で起動する(REST と同じプロセス)。`Shared/proto`・`Shared/errors.toml`・`Makefile`(`mk/codegen.mk` を読み込む)・`buf.yaml`・`buf.gen.yaml`・`Server/config/server_status.toml`。Unity に基盤のパッケージを入れ、YetAnotherHttpHandler を 1.11.6 に上げ、基盤のテストと対戦(MagicOnion)の接続を確かめる。API はまだ移さない |
| 1 | 認証とサインイン | 2章のマイグレーションの書き換え、`PlayerService`(SignUp・SignIn)、`PlayerResolver`。REST の公開 API とクライアントの REST の通信層をすべて消す(4章)。クライアントの `ApiCore`・`IAccountStore`・エラーダイアログ・`IErrorNavigator`・タイトル画面でのサインイン。BattleBot はマッチング・デバッグも REST を使うので、まとめて 5 で移す(G-1 で決めた)。消す: `/internal/battle/*` 以外の REST の API、`AccessTokenRefresher` ほか3章の表のもの |
| 2 | 編成 | `PartyService`、`PlayerDiffCollector`、クライアントの `IPlayerDiffApplier` |
| 3 | スカウト | `ScoutService` |
| 4 | デバッグ | `DebugService`(BattleBot の `RandomizeParty` も) |
| 5 | マッチング | `MatchmakingService`(`GetQueueStatus` を消さない形に変える)、`ApiBattleMatchmaker` の置き換え、BattleBot の gRPC 化(デバイス登録・サインアップ・サインイン・ランダム編成・マッチング) |
| 6 | チャット | `ChatService`(サーバーとテストだけ) |
| 7 | 後片付け | `AppError` のうち使わなくなったもの、`utoipa`・Swagger・`export_openapi`・`Shared/api/openapi.yaml`。**最後に、確認をもらってから** `api-codegen`(submodule)とスキル、Client の `Infrastructure/Api/Generated/` を外す |

- 1 を移した時点では、まだ移していない機能(編成・スカウト・マッチング等)は動かない。2 以降で順に戻す
- 各まとまりで、サーバーは tonic の生成クライアントで呼ぶ結合テスト(冪等性の対象の RPC は、同じ RequestID で2回送っても1回分しか変わらないこと)、クライアントは偽のサーバーを通すテストを書く

## 11. ドキュメント・ルールで直すもの

- ルートの CLAUDE.md: 構成図(REST → gRPC、ポート)、生成物の表(`openapi.yaml` → `Shared/proto` と `errors.toml`、`make proto-gen`・`errors-gen`)、「APIのレスポンスに `nullable` を使わない」(api-codegen の制約だったもの。基盤の規約「メッセージ型は null で変更なし / 該当なし、スカラーは番兵値」に置き換える)、「API を変えたら `api-codegen` スキル」
- Server/CLAUDE.md・Client/CLAUDE.md: 通信の層の説明(`AccessTokenRefresher` → `ApiCore`)、要認証 API の書き方
- DEVELOPMENT.md: ポート、`server_status.toml`、`make proto-gen`、生成に要るツール(.NET 10 SDK・GNU Make)、DB をリセットしたときに端末の保存ファイルを消す手順(9章 #2)
- design.md・progress.md: 通信の設計と、移したまとまりの状況
- `api-codegen` スキルを、proto の生成のスキルに置き換えるか(最後のまとまりで決める)
