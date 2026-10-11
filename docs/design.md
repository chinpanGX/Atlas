# Atlas 設計書

APIサーバー(Rust)・クライアント(Unity)・バトルサーバー(C#/MagicOnion)・DB・マスターデータの設計をまとめる。
ゲームの内容とルールは[仕様概要書](game-spec.md)、実装の進み具合と残タスクは[progress.md](progress.md)を正とする。

本書には「何を・なぜそうしたか」を書き、型やメソッドの定義そのものはソースを正とする(ソースへのリンクを置く)。
検討して採用しなかった案は、理由とともに該当する節の末尾に残す。

## 目次

1. [全体構成](#1-全体構成)
2. [共通の決まり](#2-共通の決まり)
3. [APIサーバー](#3-apiサーバー)
4. [DB](#4-db)
5. [マスターデータ](#5-マスターデータ)
6. [クライアント](#6-クライアント)
7. [バトルサーバー](#7-バトルサーバー)
8. [未確定の論点](#8-未確定の論点)

---

## 1. 全体構成

### 1.1 システム構成

```
Unity Client
   │ gRPC(VolTech gRPC)           │ MagicOnion (gRPC / StreamingHub)
   ▼                               ▼
APIサーバー(Rust/axum)        バトルサーバー(C#/MagicOnion)
 認証・プレイヤー・編成・          対戦の進行のみ(状態はメモリ上)
 スカウト・マッチング・DB書き込み        │
   ▲─────────── 内部API(REST) ──────────┘  パーティ・選出個体の取得、結果報告
   │
 MySQL(唯一のデータストア。書き込みはAPIサーバーだけが行う)
```

- サーバーは1台構成(ローカルでの動作を想定)。スケールアウトは考えない
- ClientとAPIサーバーの通信は、自作のgRPC基盤[VolTech gRPC](https://github.com/chinpanGX/VolTech-gRPC)(タグで参照)に
  乗せている。デバイス認証・トークン・再認証・エラーのダイアログ・再送しても一度だけ処理する仕組み(RequestID)・
  1リクエスト1トランザクション・メンテナンスの判定は基盤が行い、Atlasは`.proto`・業務処理・ダイアログの見た目を持つ
- DBへの書き込みはAPIサーバーに一本化する。バトルサーバーはDBを持たず、必要なデータは内部APIで
  APIサーバーから取得し、結果も内部APIで報告する
- 対戦の判定はバトルサーバーが権威を持つ(クライアントは結果を受け取って表示するだけ)
- バトルサーバーはユーザーDBを持たない。参加資格は、APIサーバーがマッチ成立時に発行する短命JWT
  (`battle_token`)を共有シークレットで検証して確かめる

### 1.2 通信の一覧

| 経路 | 方式 | 認証 | 用途 |
|---|---|---|---|
| Client → APIサーバー | gRPC(Unary)。`.proto`から生成したラッパーを基盤の`ApiCore`経由で呼ぶ([3.1](#31-構成)) | `authorization: Bearer <access_token>`(基盤が付与・更新) | 認証・所持データ・編成・スカウト・マッチング・開発用 |
| Client ⇔ バトルサーバー | MagicOnion StreamingHub(HTTP/2) | `battle_token`(JWT) | 対戦(選出・行動・結果の受信) |
| バトルサーバー → APIサーバー | REST(JSON)。gRPCとは別のポート | `X-Internal-Secret` | パーティ・選出個体の取得、結果報告 |

### 1.3 リポジトリ構成

| パス | 内容 |
|---|---|
| `Client/AtlasUnityProject/` | Unityクライアント |
| `Server/` | APIサーバー(Rust。クライアント向けはgRPC/tonic、内部APIはREST/axum) |
| `BattleServer/` | バトルサーバー(ASP.NET Core + MagicOnion)。対戦相手ボット(`BattleBot/`)とテストを含む |
| `Shared/BattleCore/` | ダメージ計算・ターン処理(`Atlas.BattleCore`)。ClientとBattleServerで同じソースを使う |
| `Shared/BattleContracts/` | Client⇔BattleServerの通信契約(`Atlas.BattleContracts`) |
| `Shared/master-data/` | マスターデータのスキーマとCSV(正本) |
| `Shared/proto/` | ClientとAPIサーバーの通信の定義(`.proto`、パッケージ`atlas.v1`。正本) |
| `Shared/errors.toml` | APIサーバーの業務エラー(domain `atlas`。正本) |
| `master-data-pipeline/`(submodule) | マスターデータの生成ツール(自作) |
| `UnityScreenNavigator/`(submodule) | 画面遷移ライブラリ(USN)のフォーク |

起動手順・環境変数は[DEVELOPMENT.md](DEVELOPMENT.md)を参照。

---

## 2. 共通の決まり

### 2.1 命名規則

- DB・Rust: `snake_case`。マスタは修飾語なし、プレイヤーの所持データは`player_`を付ける
  (`pachimon` / `player_pachimon`)
- 「ポケモン」にあたる語は`pachimon`で統一する(商標と混同しないための独自名)
- `.proto`: メッセージ・フィールドは`buf lint`(STANDARD)の規約どおり(フィールドは`snake_case`、C#では`PascalCase`になる)
- 内部API(REST)のJSON: `camelCase`(Rust側は`serde(rename_all = "camelCase")`で変換し、内部は`snake_case`のまま)
- C#(Client・BattleServer・通信契約): C#の規約どおり`PascalCase`

### 2.2 ID

プレイヤーの操作で生成されるデータのIDはULID(26文字、生成順に並ぶ)にし、DBには`CHAR(26)`の文字列で持つ。
Rustでは`ulid`クレートで生成する。所持データの「割当」(パーティの枠・覚えている技)も、行ごとにULIDを持つ
独立したエンティティとして扱う(`playerDiff`の`removed`でIDを指せるようにするため)。

マスタのIDは整数(`pachimon_id`は1001〜の4桁)。

### 2.3 nullableを使わない

gRPCのAPI(`.proto`)は基盤の規約に従う。スカラー(数値・文字列)は`optional`を使わず、「値なし」は取り決めた
番兵値(空文字など)で表す。メッセージ型のフィールドは`null`を「変更なし / 該当なし」として使ってよい
(`playerDiff`のリソースごとのフィールドなど)。

内部API(REST)のレスポンスには`nullable`を使わない。

- 「無い」ことは、行が存在しないことで表す(パーティの未編成の枠は`player_party_slots`に行を作らない)
- 値が無い文字列は空文字にする(マッチング待ちの`matchId`、勝者なしの`winnerId`)
- 状態によって中身が無いフィールドは、使う側が別のフィールド(`status`等)で判定する

BattleServerとClientの間(MessagePack)は`nullable`を使ってよい。

### 2.4 所持データの差分(`playerDiff`)

プレイヤーの所持データ(アイテム・パチモン・覚えている技・パーティ)を変えるAPIは、レスポンスに
`player_diff`(基盤の共通レスポンス項目。`Shared/proto/atlas/v1/player_data.proto`の`PlayerDiff`)を含める。
クライアントは受け取った差分を手元のデータに反映する。レスポンスの形をAPIごとに
変えると、クライアントの反映処理がAPIごとにばらばらになるため、共通の形にした。

| 種別 | 中身 | クライアントの扱い |
|---|---|---|
| 通常のレスポンス | その画面の表示に使う一時的なデータ | 表示したら捨てる |
| `playerDiff` | クライアントが持ち続ける所持データの差分 | 手元のデータに反映する |

```jsonc
"playerDiff": {
  "items":           { "upserted": [ { "itemId", "quantity" } ],                                   "removed": [ /* itemId */ ] },
  "pachimon":        { "upserted": [ { "playerPachimonId", "pachimonId" } ],                       "removed": [ /* playerPachimonId */ ] },
  "pachimonMoveMap": { "upserted": [ { "playerPachimonMoveId", "playerPachimonId", "slot", "moveId" } ], "removed": [ /* playerPachimonMoveId */ ] },
  "partySlots":      { "upserted": [ { "partySlotId", "slot", "playerPachimonId" } ],              "removed": [ /* partySlotId */ ] }
}
```

- リソースの種別ごとに`upserted`(追加・更新した行)と`removed`(削除した行のID)を持つ。
  変化が無い種別は`null`(フィールドが無い)にする
- 値は変化量ではなく変更後の値(ジェムなら残数)
- 各種別はDBのテーブルに1対1で対応する(`player_items` / `player_pachimon` / `player_pachimon_moves` /
  `player_party_slots`)。技をパチモンに内包させないのは、技の付け替えで変化していないパチモン本体まで
  送り直さないため
- ジェムのような数量も、専用のフィールドにせず`items`の1行として扱う
- 新しい所持データを足すときも、種別を1つ増やしてこの形に合わせる
- 現状は所持データを手放す手段が無いため、`items`・`pachimon`・`pachimonMoveMap`の`removed`は使われない
  (パーティの編成し直しでは`partySlots.removed`を使う)
- プレイヤーID・ニックネームは差分の考え方に合わないため`playerDiff`に含めず、必要なAPI(サインイン)が
  自分のレスポンスのフィールドとして返す
- サインインの全件は`player_diff`ではなく`snapshot`(`PlayerSnapshot`)で返す。差分としてではなく手元のデータを
  置き換える全件で、名前を分けないと基盤が差分として自動で反映してしまうため

| API | `playerDiff`の中身 |
|---|---|
| `PartyService/SetParty` | `partySlots`(新しい枠を`upserted`、解除した枠を`removed`) |
| `PartyService/SetPachimonMove` | `pachimonMoveMap`(付け替えた1件) |
| `ScoutService/Roll` | `items`(消費後のジェム) |
| `ScoutService/Select` | `pachimon`(入手した1体)と`pachimonMoveMap`(その技) |
| `DebugService/GrantGems` | `items`(付与後のジェム) |
| `DebugService/RandomizeParty` | `pachimon`・`pachimonMoveMap`(新しく付与した6体)と`partySlots` |

### 2.5 所持リソース(アイテム)

ジェムのような数量で持つリソースは、`players`に専用の列を持たせず、マスタ`items`と所持数テーブル
`player_items`で管理する。所持数は絶対値で、増減はすべてサーバーが決める。現状のアイテムはジェム
(`item_id: 1`)と育成チケット(`item_id: 2`、マスタのみで増減の処理は未実装)。

| きっかけ | 増減 | 処理する場所 |
|---|---|---|
| プレイヤー作成 | +300 | `PlayerService/SignUp` |
| 対戦の勝利 | +50 | `POST /internal/battle/result` |
| 開発用の付与 | +450 | `DebugService/GrantGems` |
| スカウトの紹介 | −(バナーの`cost_per_roll`) | `ScoutService/Roll` |

---

## 3. APIサーバー

### 3.1 構成

Rust。クライアント向けのAPIはgRPC(tonic + 基盤のクレート`voltech-grpc-*`)、BattleServer向けの内部APIはREST(axum)で、
同じプロセスの別のポートで出す(gRPCは`GRPC_ADDR`、既定`127.0.0.1:50051`。内部APIは`SERVER_ADDR`、既定`127.0.0.1:3000`)。
同じプロセスにするのは、マッチングの待機列をメモリで共有するため。ポートを分けるのは、基盤のLayer(メンテナンス・認証等)を
内部APIに付けないためと、BattleServerのHTTP/1.1の呼び出しを変えずに済むため。
ソースは`Server/src/`、詳しい開発手順は[Server/CLAUDE.md](../Server/CLAUDE.md)。

| ディレクトリ・ファイル | 役割 |
|---|---|
| `grpc/` | gRPCのサービスの実装(ハンドラ)と、サーバーの組み立て(Layerの順番・リフレクション) |
| `grpc/context.rs` | 認証の情報(`AuthContext`)からプレイヤーIDを取り出す。未作成なら`PLAYER_NOT_FOUND` |
| `proto.rs` | `build.rs`が`Shared/proto`・`Shared/errors.toml`から生成したコード |
| `api/` | 内部API(REST)のリクエスト・レスポンスの型とハンドラ |
| `service/` | 業務処理とDBアクセス。gRPCのハンドラからは基盤のトランザクションを受け取って呼ぶ |
| `model/` | DBのテーブルに対応する構造体 |
| `master/` | マスターデータの型(生成物)と、起動時に読み込むキャッシュ(`MasterData`) |
| `extractor.rs` | 内部APIの認証(`InternalService`: シークレット) |
| `error.rs` | `AppError`(サービス層のエラーと、内部APIのHTTPステータスへの変換) |
| `db.rs` | 起動時のマイグレーション(基盤のテーブル → Atlasのテーブル) |
| `state.rs` | `AppState`(DB接続プール・マスタのキャッシュ・マッチングの待機列) |
| `routes.rs` | 内部APIのルーティング |
| `config/server_status.toml` | メンテナンス・最低バージョン・ゲーム内の日付が変わる時刻(5秒ごとに読み直す) |

- gRPCのハンドラは、業務処理を基盤の`Transactions::run`の中で動かす(1リクエスト1トランザクション、冪等性の対象の
  RPCは同じRequestIDの再送で二重に実行しない)
- エラー: 業務エラーは`FAILED_PRECONDITION` + `ErrorInfo`(domain `atlas`、reasonは`Shared/errors.toml`)、
  内部エラーは`INTERNAL`(詳細はサーバーのログだけ)。内部APIは`AppError`の`BadRequest`→400、`Unauthorized`→401、
  `NotFound`→404、`Conflict`→409、`InternalError`→500
- マスターデータは起動時にDBから全件読み込み、`Arc<MasterData>`としてメモリに持つ(更新にはサーバーの再起動が要る。
  [5.3](#53-運用))
- `.proto`を変えたら、Rustはビルドのときに`build.rs`が生成し直す。Unity向けのC#はルートの`make proto-gen`・`make errors-gen`で生成する
- 開発ビルドではgRPCリフレクションを有効にし、grpcurl・Postmanから`.proto`無しで呼べる
- ログ: `tracing`でリクエストと実行したSQLを出す(`RUST_LOG`で切り替え)

### 3.2 認証

**デバイス認証(ゲスト)** — 基盤(`voltech-grpc-auth`)が行う

- 会員登録は無く、端末ごとのデバイスで認証する。クライアントが`secret_key`を作って`DeviceService/Register`で登録し、
  `device_id`と`player_id`を受け取る。以後は`device_id`と`secret_key`で`DeviceService/IssueToken`を呼び、アクセストークンを受け取る。
  どちらもクライアントの基盤(`ApiCore`)が最初の通信で自動で行う
- アクセストークンの有効期限は1時間。1つのデバイスにつき有効なトークンは常に1つ
- `player_id`はデバイス登録で発行するIDだけで、プレイヤー(`players`の行と初期データ)はサインアップで作る。
  デバイスとプレイヤーの対応は基盤の`devices`だけが持ち、`players`は`device_id`を持たない
- サーバーがプレイヤーを特定する根拠はトークンだけ(`device_id`・`player_id`はクライアントから受け取らない)
- 基盤の`AuthLayer`がトークンを検証し、Atlasの`PlayerResolver`の実装(`grpc/player_lookup.rs`)が`players`を引いて
  プレイヤーが作成済みかを返す(アカウント停止は今は無い)

**内部API**

- BattleServerからの呼び出しは`X-Internal-Secret`ヘッダーのシークレットで認証する(定数時間で比較し、
  不一致・欠落は401)。共有値は両サーバーの環境変数`INTERNAL_API_SECRET`

**`battle_token`**

- マッチ成立時にAPIサーバーが発行するHS256のJWT。claimsは`match_id`・`player_id`・`exp`で、有効期限は30秒
- 共有シークレットは環境変数`BATTLE_TOKEN_SECRET`(32バイト未満ならBattleServerは起動しない)
- BattleServerは検証の時刻のずれの許容を小さくする(既定の許容が30秒より大きいと実質の有効期限が延びるため)
- 再接続のときに限り、期限切れ(署名は正当)のトークンを受け付ける([7.4](#74-対戦の進行))

### 3.3 API一覧

**gRPC(クライアント向け)** — `.proto`のコメントが各RPCの仕様の正

| サービス / RPC | 内容 | 冪等性 |
|---|---|---|
| `voltech.v1.DeviceService/Register` | デバイスの登録(基盤) | `IDEMPOTENT` |
| `voltech.v1.DeviceService/IssueToken` | アクセストークンの発行(基盤) | `IDEMPOTENT` |
| `atlas.v1.PlayerService/SignUp` | プレイヤーの作成(初期データの付与) | `IDEMPOTENT` |
| `atlas.v1.PlayerService/SignIn` | サインイン(所持データの全件取得) | `IDEMPOTENT` |
| `atlas.v1.PartyService/SetParty` | パーティの編成(全置き換え) | 対象 |
| `atlas.v1.PartyService/SetPachimonMove` | 技の付け替え | 対象 |
| `atlas.v1.ScoutService/ListBanners` | 開催中のバナー一覧 | `NO_SIDE_EFFECTS` |
| `atlas.v1.ScoutService/Roll` | 紹介を受ける(ジェムを消費して候補10体) | 対象 |
| `atlas.v1.ScoutService/Select` | 候補から1体を選んで入手 | 対象 |
| `atlas.v1.MatchmakingService/JoinQueue` | 待機列に入る(成立すれば対戦を記録) | 対象 |
| `atlas.v1.MatchmakingService/LeaveQueue` | 待機列から抜ける | `IDEMPOTENT` |
| `atlas.v1.MatchmakingService/GetQueueStatus` | マッチが成立したかと接続情報 | `NO_SIDE_EFFECTS` |
| `atlas.v1.DebugService/GrantGems` | 開発用のジェム付与 | 対象 |
| `atlas.v1.DebugService/RandomizeParty` | 開発用のランダム編成 | 対象 |

**REST(BattleServer向けの内部API)**

| メソッド | パス | 内容 |
|---|---|---|
| POST | `/internal/battle/party` | 参加者のパーティ取得 |
| POST | `/internal/battle/loadouts` | 選出個体の所持データ取得 |
| POST | `/internal/battle/result` | 対戦結果の記録 |

- 冪等性: `idempotency_level`を書かないRPCは「対象」(クライアントはRequestIDを付けて1つずつ送り、サーバーは同じRequestIDの
  再送で二重に実行しない)。読むだけのRPCは`NO_SIDE_EFFECTS`、何回実行しても同じ結果になるRPCは`IDEMPOTENT`
- 以下の各APIでは、`playerDiff`の中身は[2.4](#24-所持データの差分playerdiff)の表を参照する

### 3.4 プレイヤー

**`PlayerService/SignUp`** — `nickname` → `player_id`・`nickname`

- デバイス登録で発行した`player_id`でプレイヤーを作る。作成済みならエラーにせず、何も変えずに作成済みのプレイヤーを返す
  (再送されても同じ結果になるように、`IDEMPOTENT`にしている)
- ニックネームは1〜50文字(範囲外は`INVALID_ARGUMENT`)
- プレイヤーの作成と同じトランザクションで次を付与する
  - スターターパーティ: マスタ`starter_party_slots`の6体をそのまま複製し、初期技込みの`player_pachimon`と
    `player_party_slots`を作る(全員共通の固定編成)
  - ジェム300(`player_items`)
  - 基盤の冪等性のためにロックする行(`last_responses`)
- 作成したデータは返さない。クライアントは続けて`SignIn`を呼ぶ

**`PlayerService/SignIn`** — 本文なし → `player_id`・`nickname`・`snapshot`(所持データの全件)

- タイトル画面で毎回呼び、所持データの全件を返す。クライアントは差分としてではなく、手元のデータを置き換える
- プレイヤーが未作成なら`FAILED_PRECONDITION` + `PLAYER_NOT_FOUND`。クライアントはこのときだけ`SignUp`を呼んでから、
  もう一度サインインする(サインアップ済みかは端末に覚えない)

### 3.5 パーティ編成・技の付け替え

**`PartyService/SetParty`** — `party_slots`(`slot`・`player_pachimon_id`の組) → `player_diff`

- 全置き換え。既存の枠を全て削除し、指定された枠を新しいULIDで作り直す(リクエストに無い枠は解除される)
- 検証: 1〜6体、slotは1〜6で重複しない、同じ個体を2つの枠に入れない(いずれも`INVALID_PARTY`)、
  自分の所持個体であること(`PACHIMON_NOT_OWNED`)。同じ種族の別の個体は何体でもよい
- 割当のIDを作り直すので、2回実行すると結果が変わる。冪等性の対象にし、再送では二重に実行しない

**`PartyService/SetPachimonMove`** — `player_pachimon_id`・`slot`・`move_id` → `player_diff`

- 技を、その種族の技グループの候補(`move_group_moves`)から選んで入れ替える。候補に無い技は`MOVE_NOT_LEARNABLE`、
  slotが1〜4の範囲外は`INVALID_MOVE_SLOT`、他人の個体は`PACHIMON_NOT_OWNED`
- `INSERT ... ON DUPLICATE KEY UPDATE`で更新する。更新の対象に`player_pachimon_move_id`を含めないため、
  既にある枠の付け替えではIDが変わらない

### 3.6 スカウト

本家の「紹介(候補10体から1体を選ぶ)」をもとにした2段階の流れ。

**`ScoutService/ListBanners`** — 本文なし → `banners`(`banner_id`・`name`・`cost_per_roll`・`start_at`・`end_at`)

- 開催中(`start_at <= 現在 <= end_at`)のバナーを開始日時の順に返す。排出率は返さない

**`ScoutService/Roll`** — `banner_id` → `roll_id`・`candidates`(`index`・`pachimon_id`・`move_ids`)・`player_diff`

1. バナーが開催中か確かめる(存在しなければ`SCOUT_BANNER_NOT_FOUND`、開催期間外は`SCOUT_BANNER_CLOSED`)
2. ジェムから`cost_per_roll`を減らす(足りなければ`INSUFFICIENT_GEMS`)
3. 候補10体を1体ずつ抽選する
   1. `rate_table`の重みでレア度を決める
   2. そのレア度の種族から等確率で1体選ぶ
   3. 技はその種族の初期技(`move_group_moves.is_initial`)4つ
4. 候補10体を`scout_rolls`に保存する(未選択)
5. 以上を1つのトランザクションで行う

**`ScoutService/Select`** — `roll_id`・`index` → `player_diff`

- 自分のロールであること(存在しない・他人のロールは`SCOUT_ROLL_NOT_FOUND`)、未選択であること
  (選択済みは`SCOUT_ROLL_ALREADY_SELECTED`)、`index`が0〜9であること(範囲外は`SCOUT_CANDIDATE_OUT_OF_RANGE`)を確かめる。
  同時に選んでも二重に入手しないよう、ロールの行をロックしてから確かめる
- 保存しておいた候補をそのまま`player_pachimon`・`player_pachimon_moves`にコピーする(再抽選しない)。
  選ばなかった9体はどこにも残らない
- ジェムはここでは変わらない(紹介の時点で消費済み)

設計上の判断:

- バナーはマスターデータ(`master-data-pipeline`)の対象外にし、APIサーバーのDBにだけ置く。開催期間を持つ
  運用寄りのデータで、クライアントを作り直さずに切り替えたいことと、排出率をクライアントに渡さないため。
  投入は専用のseed(`cargo run --bin seed_scout_banners`)で、常設の「レギュラースカウト」(150ジェム、
  S3%・A12%・B35%・C50%)を1件入れる。期間限定バナーの運用ツールは作らない
- 候補と選択結果を`scout_rolls`にまとめて記録し、紹介と入手の履歴を兼ねる
- 紹介を選ばずに放置してもジェムは戻らず、ロールの有効期限も設けない。取り直すAPIが無いため、
  クライアントは候補を選ぶまで画面を離れられないようにしている

### 3.7 マッチング

**`MatchmakingService/JoinQueue`** — 本文なし → 本文なし

- パーティが0体なら`PARTY_EMPTY`。既に待機中なら何もしない。前のマッチの接続情報が残っていれば捨ててから並ぶ

**`MatchmakingService/LeaveQueue`** — 待機列から抜け、成立していたマッチの接続情報も捨てる。いなくても成功する

**`MatchmakingService/GetQueueStatus`** — 本文なし → `status`・`match_id`・`battle_server`・`battle_token`

- 待機中(待機列にいないときも含む)は`QUEUE_STATUS_WAITING`で他は空文字([2.3](#23-nullableを使わない))。成立時は
  `QUEUE_STATUS_MATCHED`で、接続先(`battle_server`、環境変数`BATTLE_SERVER_URL`)と`battle_token`を返す
- 成立の結果は返しても消さず、次の`JoinQueue`・`LeaveQueue`か、対戦の結果の報告([3.8](#38-内部apibattleserver用))で消す。
  応答が届かずに確かめ直しても接続情報を失わず、読むだけのRPC(`NO_SIDE_EFFECTS`)にできるため

実装:

- 待機列はDBに置かず、`AppState`のメモリに`Mutex<VecDeque<player_id>>`(待機列)と
  `HashMap<player_id, MatchInfo>`(成立した結果)で持つ(1台構成のため)
- `JoinQueue`の中で同期的に組む。待っている人がいれば先頭と組み、両者分の`MatchInfo`を保存し、
  `battle_matches`に`in_progress`の行を作る(先に待っていた側が`player1`)
- `JoinQueue`はメモリの待機列を書き換えた後でトランザクションが失敗すると、メモリだけが残る。待機列への追加は二重に
  ならない作りなので、そのままにしている
- 通知はポーリング(クライアントは1秒ごと)。1秒の遅れは体験に影響せず、サーバーからの配信を入れる手間に見合わないため

### 3.8 内部API(BattleServer用)

`X-Internal-Secret`で認証する。クライアント向けのgRPCとは別のポートで出す([3.1](#31-構成))。

**`POST /internal/battle/party`** — `{ "playerId" }` → `{ "pachimon": [ { "playerPachimonId", "pachimonId" } ] }`

- BattleServerが参加を受け付けたときに呼ぶ。パーティの枠番号順(未編成の枠は含まないため0〜6件)
- 選出画面で見せるのはどのパチモンかだけなので、努力値・技は返さない

**`POST /internal/battle/loadouts`** — `{ "playerId", "playerPachimonIds" }` →
`{ "pachimon": [ { "playerPachimonId", "pachimonId", "effortValues", "moveIds" } ] }`

- BattleServerが選出を受け取ったときに呼ぶ。順番はリクエストの`playerPachimonIds`と同じ、`moveIds`はslot順
- プレイヤーごとに違う所持データだけを返し、種族値・技の性能・タイプ相性はBattleServerが自分のマスタから引く
- 所持していない個体(他人の個体・存在しないID)が1体でもあれば404

**`POST /internal/battle/result`** —
`{ "matchId", "winnerId", "player1Id", "player2Id", "player1SelectedPachimon", "player2SelectedPachimon", "turns": [ { "turnNumber", "playerId", "actionData", "resultData" } ] }` → 200

- 1つのトランザクションで、`battle_matches`の終了状態への更新、`battle_turns`の一括追加、勝者へのジェム50の
  付与を行う。対象の行を`FOR UPDATE`でロックし、二重の報告は409にする(ジェムを二重に付与しない)。
  記録できたら、マッチングの待機列に残っている両者の接続情報を消す
- `player1`/`player2`はBattleServerに先に参加した側で、マッチ成立時の順と一致するとは限らない。
  IDで`battle_matches`の参加者と突き合わせて保存する
- `winnerId`が空文字なら勝者なし: `aborted`にし、ジェムは付与しない。相手が一度も参加しなかった場合、
  その側のIDは空文字・選出は空配列
- `actionData`・`resultData`は中身を解釈せずJSONのまま保存する(HPの実数など、クライアントに送らない値も含む)
- 報告済みかどうかは選出の列がNULLでないかで判定する。後始末で`aborted`になった対戦([3.9](#39-結果報告が届かない対戦の後始末))に
  後から届いた報告は受け付け、内容で上書きする
- 400: 参加者でない`winnerId`・`player1Id`/`player2Id`・ターンの`playerId`。404: 存在しない`matchId`

### 3.9 結果報告が届かない対戦の後始末

BattleServerの再起動(対戦状態はメモリにしか無い)、両者とも一度も接続しなかった対戦、報告の再送失敗により、
`in_progress`のまま残る対戦がある。APIサーバーは5分ごとに、開始から1時間以上経った`in_progress`の対戦を
`aborted`にする(`battle_service::spawn_stale_match_cleanup`)。1時間を超える正当な対戦も打ち切られ得るが、
後から届いた結果報告で上書きされるため実害は無い。

- 採用しなかった案: BattleServerの起動時に進行中の対戦をすべて打ち切るよう通知する。再起動のケースは早く片付くが、
  定期処理だけで全ケースを賄えるため見送った

### 3.10 開発用API

正式なゲーム内機能ではなく、動作確認のためのもの(`DebugService`)。環境変数`ENABLE_DEBUG_API`が`true`・`1`のときだけ
登録する(未設定ならデバッグビルドだけ)。登録しなければ、呼ぶと`UNIMPLEMENTED`になる。

- **`DebugService/GrantGems`**: ジェム450(スカウト3回分)を付与する。回数の上限は無い。Unityのホームのデバッグボタンから呼ぶ
- **`DebugService/RandomizeParty`**: マスタから重複なしで6体を選んで新しく付与し(技は候補からランダムに最大4つ)、
  パーティを組み直す。以前の所持個体は残る。対戦相手ボットが毎回違うパーティで対戦するために使う

---

## 4. DB

### 4.1 方針

- MySQL。スキーマは`Server/migrations/`のマイグレーション(`sqlx migrate`)で管理する。基盤のテーブルと合わせて、
  APIサーバーの起動時に流す
- プレイヤーの操作で生成する行の主キーはULID(`CHAR(26)`、[2.2](#22-id))。日時は`DATETIME(3)`
- `NULL`を許す列はなるべく作らない。「無い」ことは行が無いことで表す([2.3](#23-nullableを使わない))。
  例外は`scout_rolls.selected_index`・`battle_matches`の選出・勝者・日時(状態によって値が決まる列)
- マスタのテーブル(`pachimon`等)もDBに置く。定義は[5. マスターデータ](#5-マスターデータ)
- 所持データの「割当」(パーティの枠・覚えている技)は、元のテーブルの列ではなく専用のテーブルにする。
  未設定を行が無いことで表せ、`nullable`を使わずに済むため

### 4.2 テーブル一覧

| テーブル | 内容 | 主な書き込み元 |
|---|---|---|
| `devices` | 登録されたデバイス(基盤) | `DeviceService/Register` |
| `access_tokens` | デバイスごとのアクセストークン(基盤。1デバイス1行) | `DeviceService/IssueToken` |
| `last_responses` | 冪等性の対象のRPCの直前のレスポンス(基盤。1プレイヤー1行) | 冪等性の対象のRPC・サインアップ |
| `players` | プレイヤー | `PlayerService/SignUp` |
| `player_items` | 所持アイテム(ジェム等)の数 | サインアップ・スカウト・対戦の結果 |
| `player_pachimon` | 所持しているパチモンの個体 | サインアップ・スカウト |
| `player_pachimon_moves` | 個体が覚えている技(1個体4行まで) | サインアップ・スカウト・技の付け替え |
| `player_party_slots` | パーティの編成(1プレイヤー6行まで) | サインアップ・パーティ編成 |
| `scout_banners` | スカウトのバナー(排出率・値段・期間) | `seed_scout_banners` |
| `scout_rolls` | スカウトの紹介と選択の記録 | スカウト |
| `battle_matches` | 対戦 | マッチ成立・結果報告 |
| `battle_turns` | 対戦の行動ログ | 結果報告 |

### 4.3 認証・プレイヤー

**基盤のテーブル(`devices`・`access_tokens`・`last_responses`)**

デバイス・アクセストークン・冪等性のための直前のレスポンスは、gRPC基盤のクレートのマイグレーションが作る
(履歴は`_auth_migrations`・`_transaction_migrations`。Atlasの`_sqlx_migrations`とは別)。列は基盤が決めるので
ここには書かない。基盤のテーブルの`player_id`は`BINARY(16)`で、Atlasのテーブルの`CHAR(26)`とはつなぎ目で変換する。

**players**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_id` | CHAR(26) | PK | デバイス登録で基盤が発行したULID |
| `nickname` | VARCHAR(50) | NOT NULL | |
| `created_at` | DATETIME(3) | NOT NULL | |

ジェム等の数は持たない(`player_items`で持つ)。デバイスとの対応は基盤の`devices`だけが持つので、`device_id`は持たない。

### 4.4 所持データ

**player_items**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_id` | CHAR(26) | PK(複合), FK → `players` | |
| `item_id` | INT | PK(複合), FK → `items` | `1`がジェム |
| `quantity` | INT | NOT NULL | 所持数(絶対値) |
| `updated_at` | DATETIME(3) | NOT NULL | |

増減は`INSERT ... ON DUPLICATE KEY UPDATE`で行う。個々の行を独立したエンティティとして指す必要が無いため、
ULIDではなく`(player_id, item_id)`の複合主キーにしている。

**player_pachimon**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_pachimon_id` | CHAR(26) | PK | |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | |
| `pachimon_id` | INT | NOT NULL, FK → `pachimon` | 種族 |
| `effort_values` | JSON | NOT NULL | 能力に割り振ったポイント。`{"hp","atk","def","spatk","spdef","speed"}`、入手時はすべて0 |
| `obtained_at` | DATETIME(3) | NOT NULL | |

- レベルは列を持たない(全員50の固定値で、BattleCoreの定数)。個体値も持たない
- `effort_values`は仕様概要書のステータスポイント(64ポイント)の入れ物。割り振る機能は追加予定で、
  計算式への反映の仕方も未確定([8](#8-未確定の論点))

**player_pachimon_moves**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_pachimon_move_id` | CHAR(26) | PK | 割当のID。付け替えでは変わらない |
| `player_pachimon_id` | CHAR(26) | NOT NULL, FK → `player_pachimon` | |
| `slot` | INT | NOT NULL | 1〜4。`UNIQUE(player_pachimon_id, slot)` |
| `move_id` | INT | NOT NULL, FK → `moves` | |

個体を作るとき(スターター・スカウト)に、技グループの初期技4つを複製する。

**player_party_slots**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `party_slot_id` | CHAR(26) | PK | 割当のID。編成し直すたびに作り直す |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | |
| `slot` | INT | NOT NULL | 1〜6。`UNIQUE(player_id, slot)` |
| `player_pachimon_id` | CHAR(26) | NOT NULL, FK → `player_pachimon` | `UNIQUE(player_id, player_pachimon_id)`(同じ個体を2つの枠に入れない) |
| `created_at` | DATETIME(3) | NOT NULL | |

- 採用しなかった案: `player_pachimon`に`party_slot`(NULL可)の列を持たせる。未編成を`NULL`で表すことになり、
  APIに`nullable`が出てくるため

### 4.5 スカウト

**scout_banners**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `banner_id` | CHAR(26) | PK | |
| `name` | VARCHAR(100) | NOT NULL | |
| `rate_table` | JSON | NOT NULL | レア度ごとの確率(合計1.0)。例: `{"S":0.03,"A":0.12,"B":0.35,"C":0.50}` |
| `cost_per_roll` | INT | NOT NULL | 紹介1回のジェム |
| `start_at` / `end_at` | DATETIME(3) | NOT NULL | 開催期間 |

**scout_rolls**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `roll_id` | CHAR(26) | PK | |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | |
| `banner_id` | CHAR(26) | NOT NULL, FK → `scout_banners` | |
| `candidates` | JSON | NOT NULL | 候補10体。各要素は`{"pachimonId","rarity","moves"}` |
| `selected_index` | INT | NULL可 | 選んだ候補(0〜9)。未選択は`NULL` |
| `created_at` | DATETIME(3) | NOT NULL | 紹介を受けた日時(ジェム消費の日時) |
| `selected_at` | DATETIME(3) | NULL可 | 選んだ日時 |

### 4.6 対戦

**battle_matches**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `match_id` | CHAR(26) | PK | |
| `player1_id` / `player2_id` | CHAR(26) | NOT NULL, FK → `players` | `player1`は先に待機列にいた側 |
| `status` | ENUM('matching','in_progress','finished','aborted') | NOT NULL | マッチ成立時に`in_progress`で作る(`matching`は使わない)。`aborted`は勝者なしの終了 |
| `winner_id` | CHAR(26) | NULL可, FK → `players` | `finished`のときだけ |
| `player1_selected_pachimon` / `player2_selected_pachimon` | JSON | NULL可 | 選出した`player_pachimon_id`(選んだ順)。結果報告で必ず設定するため、NULLなら未報告 |
| `started_at` / `ended_at` | DATETIME(3) | NULL可 | |

**battle_turns**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `turn_id` | CHAR(26) | PK | |
| `match_id` | CHAR(26) | NOT NULL, FK → `battle_matches` | |
| `turn_number` | INT | NOT NULL | |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | 行動した側 |
| `action_data` | JSON | NOT NULL | `{"type": "Move"/"Switch"/"Skip", "moveId", "partySlot"}` |
| `result_data` | JSON | NOT NULL | `{"hit","critical","effectiveness","damageDealt","targetRemainingHp","targetFainted","newActiveIndex"}` |
| `created_at` | DATETIME(3) | NOT NULL | |

- 1ターン内の行動ごとに1行
- マッチングの待機列はDBに置かない(APIサーバーのメモリ、[3.7](#37-マッチング))
- 報酬の付与履歴のテーブルは持たない(`winner_id`と固定額から計算し直せるため)
- バトルサーバーのIDは持たない(1台構成のため)

---

## 5. マスターデータ

### 5.1 生成と配布

正本は`Shared/master-data/`のスキーマ(`schema/tables/*.yaml`・`schema/enums/*.yaml`)とCSV。
自作の`master-data-pipeline`(submodule)がこれを読み込み、各所向けの生成物を作って配置する。
手順は`master-data-pipeline`スキルと、テーブル・Enumの追加は`master-data-schema-add`スキルを参照。

| 配布先 | 生成物 | 読み込み方 |
|---|---|---|
| Client | C#の型(Models/Enums、MasterMemory + MessagePack)、ローダー、`masterdata.bytes`(暗号化) | 起動時の初期化(`MasterDataInitializationTask`)で`MasterDatabaseLoader`がAddressablesから読み込む。`MemoryDatabase`はRootのスコープに、読み込み済みのものを返すシングルトンとして登録してあり、Presenter等はコンストラクタで受け取る(初期化より前に要求すると例外) |
| バトルサーバー | Clientと同じC#の型・ローダー・`masterdata.bytes`(`BattleServer/MasterData/`) | 起動時にファイルから読み込み、`MemoryDatabase`をシングルトンで持つ。読み込めなければ起動しない |
| APIサーバー | Rustの型(`src/master/generated/`)とJSON | `seed_master_data`でDBへUPSERTし、サーバー起動時にDBから全件読み込む |

- ClientとBattleServerには同じ生成物をそれぞれにコピーする。共有パッケージは作らない
  (どちらも正本から毎回生成されるため食い違わない。BattleServerがClientのフォルダを参照するのは、
  サーバーからクライアントへの依存になるため採らない)
- C#の型・ローダーは1つのアセンブリにまとめる(Clientは`Atlas.MasterData.asmdef`、BattleServerは
  `Atlas.MasterData.csproj`)。MasterMemory・MessagePackのSource Generatorが同じアセンブリ内でしか働かないため
- 生成される型は「テーブル名のPascalCase + `Data`」(`pachimon` → `PachimonData`)で、`partial`。
  ゲームロジックはこの型を直接使わず、BattleCoreの型(`ParticipantStats`/`MoveData`)に変換して使う
- テーブルごとに配布先(`targets`)を決められる。サーバーだけで使うもの(`move_groups`・`starter_party_slots`・`shop_products`)は
  Clientの`masterdata.bytes`に含めない(Clientは`pachimon.move_group_id`から`move_group_moves`を直接引くため、
  `move_groups`自体は要らない)
- パイプラインの型は`int`/`long`/`string`/`bool`/`enum`だけで、小数・`nullable`・複合主キーを持てない。
  スキーマはこの制約に合わせる(下記の各テーブル)

### 5.2 テーブル

本家ポケモンの対戦から、天候・フィールド・持ち物・特性・技の追加効果を外した構成。レベルアップで技を
覚える仕組みは無く、パチモンに「技グループ」を紐付ける。

**pachimon**(136件)

| 列 | 説明 |
|---|---|
| `pachimon_id` | PK。4桁(1001〜) |
| `name` | |
| `primary_type` / `secondary_type` | `pachimon_type`。2つ目が無い場合は`NONE`(`nullable`の代わり) |
| `base_hp` / `base_atk` / `base_def` / `base_spatk` / `base_spdef` / `base_speed` | 種族値 |
| `rarity` | `rarity`(S/A/B/C)。スカウトの抽選に使う |
| `move_group_id` | FK → `move_groups` |

**move_groups**

| 列 | 説明 |
|---|---|
| `move_group_id` | PK |
| `name` | 管理用の名前 |

複数のパチモンで共有できる設計だが、現状のデータは1パチモン1グループ(`pachimon_id`と同じ値)。

**move_group_moves**(技グループが含む技。現状は各グループ20〜48件)

| 列 | 説明 |
|---|---|
| `unique_id` | PK。パイプラインが複合主キーに対応しないための代理キー |
| `group_id` | FK → `move_groups` |
| `move_id` | FK → `moves` |
| `is_initial` | 初期技か。各グループちょうど4件(APIサーバーのテストで確かめている) |

**moves**(261件)

| 列 | 説明 |
|---|---|
| `move_id` | PK |
| `name` | |
| `move_type` | `pachimon_type`(Rustの予約語`type`を避けた列名) |
| `category` | `move_category`(physical/special/status) |
| `base_power` | 威力。状態技は0(`nullable`の代わり) |
| `accuracy` | 命中率 |
| `max_pp` | |

状態技は定義できるが、追加効果が無いため現状は効果なし。初期技は物理・特殊技だけにする。

**type_chart**

| 列 | 説明 |
|---|---|
| `attack_type` / `defend_type` | 複合PK、`pachimon_type` |
| `effectiveness` | `type_effectiveness`(IMMUNE / NOT_VERY_EFFECTIVE / NORMAL / SUPER_EFFECTIVE) |

- パイプラインが小数を持てないため、倍率ではなく区分で持つ。区分 → 倍率(0/0.5/1/2)の変換と、2タイプの
  かけ合わせはBattleCoreに書く(ダメージを計算するのはBattleCoreだけで、APIサーバーには不要)
- 「行が無ければ1倍」という暗黙の既定値は作らず、全タイプ×全タイプの組み合わせをCSVに書く
- DBには入れない(使うのはClientとBattleServerだけ)

**starter_party_slots**

| 列 | 説明 |
|---|---|
| `slot_no` | PK(1〜6) |
| `pachimon_id` | FK → `pachimon` |

新規プレイヤーに複製する固定の6体(現状はレア度Cの6体)。

**items**

| 列 | 説明 |
|---|---|
| `item_id` | PK。`1`がジェム、`2`が育成チケット |
| `name` | |

**shop_products**

| 列 | 説明 |
|---|---|
| `shop_product_id` | PK |
| `item_id` | FK → `items`。購入で受け取るアイテム |
| `amount` | 1回の購入で受け取る数 |
| `cost_item_id` | FK → `items`。支払うアイテム(現状はジェム) |
| `cost_amount` | 1回の購入で支払う数 |

- マスタにある商品はいつでも買える(販売期間を持たない)。パイプラインに日時の型が無いこともあり、期間を持つスカウトの
  バナーとは違ってマスタで持つ
- 購入の可否と支払う数はサーバーが判定するため、Clientには配布せず、購入できる商品を通信で返す
- DBのテーブル(マイグレーション)と`seed_master_data`への投入は未作成

**Enum**: `pachimon_type`(18タイプ + `NONE`)、`rarity`、`move_category`、`type_effectiveness`
(`Shared/master-data/schema/enums/`)。

### 5.3 運用

- APIサーバー: `cargo run --bin seed_master_data`でDBへUPSERTし、サーバーを再起動する
- Client・BattleServer: パイプラインで生成して配置し、再起動する(Clientは作り直し)
- 無停止での反映は、1台構成の規模では過剰なため入れない
- Clientへの配布をCDN経由にする案は保留(バージョン管理・差分検知を決めていない)
- スカウトのバナーはマスターデータではない([3.6](#36-スカウト))

---

## 6. クライアント

Unity(`Client/AtlasUnityProject/`)。コーディング規約は[Client/AtlasUnityProject/CLAUDE.md](../Client/AtlasUnityProject/CLAUDE.md)。

### 6.1 採用ライブラリ

ビルドはIL2CPPを前提にする。導入経路が3つあり、ライブラリごとに異なる。

| 用途 | ライブラリ | 導入経路 |
|---|---|---|
| DI | VContainer | OpenUPM |
| 非同期 | UniTask | OpenUPM |
| リアクティブ(View→Presenterの通知) | R3 | Git |
| UIのアニメーション | LitMotion | Git |
| アセット管理 | Addressables | Unity |
| 画面遷移 | UnityScreenNavigator(USN)のフォーク | submodule(`file:`) |
| 共通ユーティリティ(ローダー、暗号化セーブ、`IMessageBroker`等) | Supplement | Git(タグ) |
| 画面をまたぐ通知 | ZeroMessenger(Supplementの`IMessageBroker`経由でだけ使う) | NuGetForUnity |
| マスターデータ | MasterMemory + MessagePack(本体とSource Generator) | NuGetForUnity |
| 対戦の通信 | MagicOnion.Client + YetAnotherHttpHandler + Grpc.Net.Client | OpenUPM + NuGetForUnity + UnityNuGet |
| APIサーバーとの通信 | VolTech gRPC(自作の基盤。`ApiCore`・認証・エラーダイアログの仕組み・gRPC Monitor)+ `.proto`から生成したラッパー | Git(タグ)+ 生成コード |
| テスト | Unity Test Framework | Unity |

- MagicOnion・MasterMemory・MessagePackは、UPM版が薄いラッパーでSource Generatorを含まないため、
  NuGet版(本体 + Source Generator)をNuGetForUnityで入れている
- APIサーバーとの通信と対戦の通信は、同じGrpc.Net.Client(UnityNuGet)とYetAnotherHttpHandlerを使う
- 対戦の通信は、標準の`SocketsHttpHandler`ではなくYetAnotherHttpHandlerを使う。IL2CPPで標準のHTTP/2
  (ALPN)が不安定なため、Editor・IL2CPPとも同じネイティブのHTTP/2実装にそろえる
- 画面遷移は、Push/Popのスタック型で足りる規模のため、フロー制御(名前付きルート等)を持つ自作の
  フレームワークは作らずUSNを使う。フォーク([chinpanGX/UnityScreenNavigator](https://github.com/chinpanGX/UnityScreenNavigator)、
  `develop`ブランチ)で、Presenter起点の画面遷移(VContainer連携)・Overlay・遷移の直列化を足している
- スクリプト定義シンボル`USE_VCONTAINER`(SupplementのVContainer連携)と`USN_USE_ADDRESSABLES`(USNのAddressablesローダー)を
  有効にしている。Supplementの`IMessageBroker`は別パッケージ(`com.chinpangx.supplement.zeromessenger`)で、
  `manifest.json`に個別に追加している
- 採用しなかった案: MessagePipe。Supplementに同じ種類の仕組み(`IMessageBroker`)が既にあり、二重になるため

### 6.2 シーン構成

`Bootstrap` / `Home` / `Battle`の3シーン。

```
Bootstrap(ビルド設定の起動シーン。Addressablesの対象外。一度もアンロードしない)
  RootLifetimeScope: マスタの読み込み、通信の共通処理(ApiCore)、Connection・Repository・ServiceのDI登録、エラーダイアログ
    │ 完了後、Homeへ
    ▼
Home(Addressables)                              Battle(Addressables)
  HomeLifetimeScope                               BattleLifetimeScope
  USNのPage/Modal/Overlayのコンテナ     ⇄     USNのPage/Modal/Overlayのコンテナ
  タイトル・ホーム・パーティ編成・スカウト        選出・対戦・交代・投了・結果
```

- シーンの切り替え(Bootstrap→Home、Home⇔Battle)は`ISceneNavigator`、シーンの中の画面遷移はUSNの
  Page/Modalで行う([6.4](#64-画面遷移))
- Bootstrapをアンロードしないことで、`RootLifetimeScope`を`DontDestroyOnLoad`等を使わずに常駐させる
- 次のシーンを読み込んでから前のシーンを外す(切り替え中に何も無い瞬間を作らない)
- Homeシーンは、サインインしていなければタイトルから、していればホームから始まる([6.6](#66-通信と所持データ))
- Home→Battleへの対戦情報(`BattleMatch`)の受け渡しは、Root常駐の`BattleEntryStore`を通す
  (シーンを切り替えるとHome側のスコープは破棄されるため)
- 配置: Page/Modalのprefabは`Assets/Addressables/Views/{機能名}/`、機能をまたぐ部品のprefabは
  `Views/Parts/`、Home/Battleシーンは`Assets/Addressables/Scenes/`、Bootstrapは`Assets/Scenes/`
- `SheetContainer`(タブ)は使う画面が無いため入れていない

### 6.3 画面の構成(View / Presenter / Service)

「Viewのユーザー操作 → Presenterが購読 → Service(またはConnection)を呼ぶ → 結果からViewDtoを作る →
Viewを更新する」という一方向の流れにそろえる。

| 役割 | 型 | 置き場所 | 内容 |
|---|---|---|---|
| View | `XxxPage` / `XxxModal`(USNの`Page`/`Modal`を継承) | `Atlas.Presentation` | ボタン等を`Observable`で公開し、`Refresh(dto)`で表示に反映するだけ。Serviceやマスタの型を知らない |
| Presenter | `XxxPresenter`(フォークの`IPresenter`) | `Atlas.Presentation` | 具象のViewとService等をコンストラクタで受け取り、Viewを購読する |
| 表示データ | `XxxViewDto` | `Atlas.Presentation` | Push時の入力と、Viewの表示データを兼ねる |
| Service | `IXxxService` | `Atlas.Application`(実装は`Atlas.Infrastructure`) | 複数の画面から使う処理。Presenterはインターフェースで受け取る |

- Viewにインターフェース(`IXxxView`)は作らない。PresenterとViewは同じ画面のための1対1の組で、
  抽象化しても差し替える場面がほぼ無いため。一方Serviceは複数の画面で使い、中身(Mock/Real)を
  差し替えるため、インターフェースにする(参考: [Mirrativの記事](https://tech.mirrativ.stream/entry/2023/09/22/112042)、
  [OutgameSample](https://github.com/mr-imada/OutgameSample))
- 依存はPresenter→Viewの一方向。ViewはPresenterを知らない。Page/Modalのprefabに`LifetimeScope`は置かない
- 現状、Serviceが素通りになるだけの処理は、PresenterがConnectionを直接使っている(スカウトの
  `IScoutConnection`、ホームのマッチング`IBattleMatchmaker`・開発用`IDebugConnection`)
- ViewDtoの粒度(画面全体を作り直すか、差分だけ渡すか)は画面ごとに決める。アウトゲームは全体、
  バトルは差分寄り(`BattlePage`は状態の`BattleUIStateDto`と、メッセージ・演出用のメソッドを分けている)

**DIとライフサイクル**

```
screenNavigator.PushPageAsync<XxxPresenter>()          -- [AssetAddress]からprefabのアドレスを取る
  → USNがprefabを生成
  → 子スコープを作り、View・ViewDto・Presenterを登録してPresenterを解決
  → InitializeAsync(開くアニメーションの前) → ... → Pop時にCompleteAsync → 子スコープごとDispose
```

- スコープは3段: Root(アプリの生存期間) → シーン(`HomeLifetimeScope`/`BattleLifetimeScope`、
  `FindParent()`でRootを直接親にする) → Push単位の子スコープ(画面の表示期間。`ScreenNavigator`が
  `CreateScope`で作るヘッドレスなスコープ)
- シーンのスコープには、そのシーンのUSNのコンテナと`IScreenNavigator`を登録する。Presenterはコンテナを
  直接触らず`IScreenNavigator`で遷移する
- **`InitializeAsync`が終わるまで開くアニメーションが始まらない**。通信のように時間がかかる処理
  (スカウトのバナー取得、BattleServerへの参加)は`InitializeAsync`で待たず、開始だけして先に画面を開く。
  また`InitializeAsync`の中でシーンの切り替えを待つと、切り替えがPush自身の完了を待つためデッドロックする
- Presenterは`Dispose`で購読を解放するだけでよい(子スコープの破棄で呼ばれる。自分で呼ばない)
- Service・Connection・Repositoryのメソッドは`CancellationToken`を受け取らない。RootのSingletonで、
  特定の画面の表示期間に結び付かないため。途中でやめたい場合はPresenter側で待つのをやめる
  (対戦の`IBattleConnection`はセッション単位のため例外)

### 6.4 画面遷移

フォークのUSNの`IScreenNavigator`をそのまま使う(Atlas側に独自の抽象は置かない)。定義はフォークのソースと
README、設計はフォーク側の`feature-screen-navigation-package.md`を参照。

- **Push**: 引数のある画面のPresenterは`IScreenWithArgs<XxxViewDto>`を実装し、
  `PushXxxAsync<TPresenter, TArgs>(args)`で開く(型の組み合わせはコンパイル時に検査される)。引数の無い画面は
  `IPresenter`だけを実装する。`stack: false`で直前のPageを破棄する(戻る必要の無い遷移で使う)
- **Pop結果**: 結果を返す画面のPresenterは`CompleteAsync()`で結果を返し、Push元は`WaitForPopAsync<TResult>`で
  受け取る。結果の型は実行時に検査される(`object`で返すため、型が違うと`InvalidCastException`)。
  ボタン以外で閉じた場合(背景タップ、シーンごと破棄)も待機は終わり、その時点の値を返す
- **二重にPopしない**のはアプリ側の責務。ボタンは`Take(1)`で1回だけ受け付ける。Push元からも閉じる画面
  (交代選択・選出)は、最初の結果を優先して2回目以降は進行中のPopを待つだけの`CloseAsync`を持たせる
- 結果を返さない画面から戻ったことを知りたいだけなら、Push元の`WillPopEnterAsync`を使う
  (ホームがスカウトから戻ったときにジェムを取り直す)
- 表示だけ・操作をPush元に渡すだけのModalにも、Pushの型として薄いPresenterを用意する
- Overlay(Modalより手前のレイヤー)はコンテナだけ用意してあり、使う画面はまだ無い(通信エラーのダイアログはOverlayではなくBootstrapシーンに置く。[6.9](#69-通信エラーダイアログ))

**遷移の直列化**

- フォークの`ScreenNavigator`はコンテナごとに遷移を順番待ちにする。USNは遷移中のPush/Popを例外で拒否するため、
  別々の場所から来た遷移(交代選択を閉じている途中に結果のModalを出す等)が重なっても失敗させない
- ユーザーの連打は対象外(直列化すると押した回数だけ画面が開くため)。遷移中はUSNの設定で全コンテナの入力を止め、
  遷移を始める前(通信を待つ間など)はPresenterで`AwaitOperation.Drop`や`Take(1)`を使う

**シーンの切り替え**

- `ISceneNavigator`にはアプリ側の`TransitionAwareSceneNavigator`(`Atlas.Navigation`)を登録する。
  全コンテナの遷移が終わるのを待ってから、フォークの`SceneNavigator`に委ねる。USNの遷移アニメーションは
  `DontDestroyOnLoad`のオブジェクトで動くため、遷移中にシーンごと画面を破棄すると例外になるため
- 切り替え中は前後のシーンが同時に読み込まれている。USNのコンテナは名前を静的な辞書に登録するため、
  Home/Battleのコンテナの名前は空にしておく

**命名**

- 画面ごとの型は画面名をプレフィックスにそろえる(`XxxPage`/`XxxModal`、`XxxPresenter`、`XxxViewDto`、
  Pop結果の`XxxResult`)
- Presenterには`[AssetAddress(AddressDefinition.Xxx)]`を付ける。`AddressDefinition`はAddressablesから
  自動生成する定数で、Page/Modalを足したら生成し直す
- prefabのアドレスは、対応するViewのクラス名と同じにする

### 6.5 画面をまたぐ通知

- 同じ画面の中の通知は、R3の`Observable`を直接購読するだけにする(pub/subは使わない)
- どの画面から・どの画面へと決まらない通知(トースト、ジェムの残数の変化など)だけ、Supplementの
  `IMessageBroker`を使う。実装(`GlobalMessageBroker`)は型ごとの静的なチャンネルに委譲するだけなので、
  どのスコープで解決しても同じチャンネルにつながる。登録はRootで1回
- メッセージは`struct`で、通知の内容を表す名前にする(`GemsBalanceChangedMessage`。画面名は付けない)
- Pushした画面から結果を受け取るのは`WaitForPopAsync`で行い、`IMessageBroker`は使わない

### 6.6 通信と所持データ

サーバーとの通信と、クライアントが持つ所持データを別の抽象に分ける。

| 抽象 | 置き場所 | 内容 | 差し替え |
|---|---|---|---|
| `IXxxConnection` | `Atlas.Application` | サーバーのAPIの呼び出し(`IPlayerConnection`/`IPartyConnection`/`IScoutConnection`/`IDebugConnection`)。Domainの型で受け渡す | 差し替えない(テストは送り先の`CallInvoker`を偽のサーバーにする) |
| `IXxxRepository` | `Atlas.Domain` | 手元の所持データの読み書き。`playerDiff`の種別ごとに1つ(`IItemRepository`/`IPachimonRepository`/`IPachimonMoveMapRepository`/`IPartyRepository`)。ほかに`IPlayerProfileRepository` | 切り替えない |
| `IXxxService` | `Atlas.Application` | Presenterが使う処理(`ISignInService`/`IPlayerAccountService`/`IItemFetchService`/`IPartyService`/`IPachimonService`/`IPachimonMoveMappingService`) | 切り替えない(実装は1つ) |
| `IPlayerSession` | `Atlas.Application` | サインインしているか。タイトルに戻るときに手元の所持データを捨てる | 切り替えない |

- 通信の共通処理は、gRPC基盤の`ApiCore`が受け持つ(トークンの付与・期限前の発行し直し・`UNAUTHENTICATED`のときの1回だけの再送、
  エラーの変換とダイアログ、RequestIDと順番待ち、サーバー時刻・ゲーム内の日付、`player_diff`の反映)。
  Connectionの実装(`Atlas.Infrastructure.Grpc`)は、`.proto`から生成したラッパー(`PlayerApi`等)を呼び、生成の型とDomainの型を詰め替えるだけ
- Connectionは成否(または結果の型)だけを返し、例外は投げない。失敗したときのダイアログ・画面遷移は`ApiCore`が済ませている。
  画面で自分で処理したいエラー(サインインの`PLAYER_NOT_FOUND`)だけ、Connectionの実装が`handledCodes`で受け取る
- 対戦は、マッチング(APIサーバー)とバトル本体(MagicOnion)で生存期間が違うため分ける。
  `IBattleMatchmaker`(待機列への参加から成立までのポーリング。実装は`GrpcBattleMatchmaker`)はRootのSingleton。
  `IBattleConnection`は対戦ごとの接続のため、`IBattleConnectionFactory`をSingletonにしてBattleシーンのスコープで生成する
- `GrpcBattleMatchmaker`は、通信が失敗したらnullを返し、キャンセルされたら待機列から抜けて`OperationCanceledException`を投げる。
  基盤の`ApiCore`は1回の通信を途中で止める口を持たないので、キャンセルは状況の確認の合間で見る

**所持データの反映**

- `player_diff`は、種別ごとの`XxxDiffApplier`が`upserted`/`removed`を対応するRepositoryの`Upsert`/`Delete`に変換する
  (`PlayerDiffApplier`が振り分ける)。変わらない種別は`null`で届くので飛ばす。`PlayerDiffApplier`は基盤の`IPlayerDiffApplier`として
  `ApiCore`に渡し、`ApiCore`が成功したレスポンスを呼び出し側に返す前に反映する。Connection・Serviceは反映に関わらない
- サインインの全件(`snapshot`)は、`PlayerSnapshotApplier`が各Repositoryを空にしてから入れる(置き換え)。全件には`removed`が無く、
  先に消さないとサーバーで無くなったものが残るため
- 汎用のDiff型・汎用のRepositoryは作らず、種別ごとに`XxxDiffApplier`とRepositoryを1組ずつ足す
- Repositoryはメモリに持つだけでディスクには保存しない。サーバーのDBが正で、サインインで全件を取り直せるため
- 例外はアカウント情報(`device_id`・`secret_key`・`player_id`)で、基盤の`IAccountStore`をAtlasが実装した
  `FileAccountStore`が、Supplementの暗号化セーブで接続先ごとのファイルに保存する(基盤の既定はメモリだけで、
  起動するたびに新しい端末になるため)

**サインインの流れ**

起動時の初期化(`BootInitializer`)ではマスタだけを読み込み、Homeへ進む。Homeシーンは、サインインしていなければ
タイトルから始まる(`IPlayerSession.IsSignedIn`)。タイトルの「スタート」でサインインする。

```
TitlePresenter(スタート)
  └─ ISignInService.SignInAsync()
       ├─ IPlayerConnection.SignInAsync     PlayerService/SignIn(最初の通信でApiCoreがデバイス登録・トークンの発行を行う)
       │    └─ PlayerSnapshotApplier         → 各Repository(置き換え)
       ├─ (PLAYER_NOT_FOUNDなら)IPlayerConnection.SignUpAsync → もう一度SignIn
       ├─ IPlayerProfileRepository           プレイヤーID・ニックネーム
       └─ IPlayerSession.MarkSignedIn()
  → HomePresenterをPush(タイトルは履歴に積まない)
```

- サインインを起動処理から外したのは、通信エラーのダイアログや「タイトルへ」の遷移を基盤が出すため。起動処理の中では
  タイトルへ戻る先が無い
- サインアップ済みかは端末に覚えず、毎回サインインの結果で判断する
- `IPlayerSession`を`ISignInService`と分けているのは、エラーの後の画面遷移(`ErrorNavigator`)が`ApiCore`から呼ばれるため。
  サインイン(通信)に依存すると、`ApiCore` → `ErrorNavigator` → `ISignInService` → Connection → `ApiCore`と循環する

**テストでの差し替え**

- `RootLifetimeScope`の`CreateCallInvoker`(virtual)に通信の送り先を切り出し、本番は`ApiChannel`(gRPC)。
  Battle PlayModeテスト用の派生スコープ(`TestRootLifetimeScope`)は、基盤の`FakeCallInvoker`に偽のサーバー(`FakeServer`)を
  登録したものに差し替える。ラッパー・`ApiCore`・反映処理は本物と同じに動く
- 対戦(`IBattleMatchmaker`/`IBattleConnectionFactory`)は`ConfigureBattleConnections`(virtual)に切り出し、
  Bootstrapシーンの`RootLifetimeScope`のInspector(`Use Battle Server`)で切り替える。オンで実サーバー、
  オフで`MockBattleConnection`(BattleCoreをクライアント内で動かし、相手は簡易AI)
- 採用しなかった案: Connectionの単位でMockを作る。ラッパーと`ApiCore`を通らず、エラー処理・反映処理が本物と違う動きになるため

### 6.7 アセンブリとフォルダ

オニオンアーキテクチャ。Domainを中心に、Application → Presentation・Infrastructure → DI(構成ルート)の順に外側になり、
依存は外側から内側への一方向だけにする(asmdefの参照で強制する)。通信・保存のインターフェースは内側に置き、Infrastructureが実装する。

| asmdef | 置くもの | 参照 |
|---|---|---|
| `Atlas.Domain` | Entity、`IXxxRepository`、`IBattleConnection`とその型 | UniTask |
| `Atlas.Application` | `IXxxService`、`IXxxConnection`、`IPlayerSession`、`IBattleMatchmaker`/`IBattleConnectionFactory`、戻り値の型、`BattleEntryStore`、`AddressDefinition` | Domain, MasterData, UniTask |
| `Atlas.Infrastructure` | Serviceの実装、Repositoryの実装、`PlayerSession`、`FileAccountStore`、`MasterDatabaseLoader` | Domain, Application, MasterData, Supplement, VolTech.Client.Auth |
| `Atlas.Infrastructure.Grpc` | Connectionの実装、`GrpcBattleMatchmaker`、`PlayerDiffApplier`/`XxxDiffApplier`/`PlayerSnapshotApplier` | Domain, Application, Atlas.Api.V1, VolTech.Client |
| `Atlas.Api.V1` | `.proto`・`errors.toml`から生成したC#(`Infrastructure/Grpc/Generated/`。手で編集しない) | VolTech.Client, VolTech.V1 |
| `Atlas.Infrastructure.Mock` | 偽のサーバー`FakeServer`、`MockBattleConnection`/`MockBattleMatchmaker` | Domain, Application, MasterData, BattleCore, Atlas.Api.V1, VolTech.Client |
| `Atlas.Infrastructure.Realtime` | `RealtimeBattleConnection`(MagicOnionのクライアント) | Domain, Application, BattleContracts, BattleCore, MagicOnion |
| `Atlas.Navigation` | `TransitionAwareSceneNavigator`、`ErrorNavigator`(通信エラーの後の画面遷移) | USN, UniTask, Application, VolTech.Client |
| `Atlas.Presentation` | View・Presenter・ViewDto(画面単位の型だけ)、`ErrorDialog` | Domain, Application, MasterData, USN, VContainer, R3, VolTech.Client |
| `Atlas.DI` | `RootLifetimeScope`/`HomeLifetimeScope`/`BattleLifetimeScope`(構成ルート) | すべて |
| `Atlas.MasterData` | マスターデータの生成物([5.1](#51-生成と配布)) | MasterMemory, MessagePack |
| `Atlas.BattleCore` / `Atlas.BattleContracts` | ローカルパッケージ(`Shared/`、[7.1](#71-構成)) | |

- `Atlas.DI`だけが全レイヤーに依存してよい最上位のアセンブリ。シーン単位の`LifetimeScope`はすべてここに置く。
  依存は常に`Atlas.DI`→各レイヤーの一方向で、`Atlas.Presentation`は`Atlas.Infrastructure`も`Atlas.DI`も参照しない
- Presenterは`IXxxService`・`IXxxConnection`(Application)越しにしか通信しないため、実装がMockかRealかを知らない

**`Atlas.Presentation`のフォルダ**(`Assets/Scripts/Presentation/`)

```
Common/        機能に属さない共通の型(PaletteColor、PachimonTypeNames等)
Title/
Home/          HomePage・HomePresenter・HomeViewDto
  Matchmaking/ Modal(1Modal = 1フォルダ)
Party/         パーティ編成。ほかの機能でも使う部品(PachimonInfoView等)もここ
Scout/
  Confirm/
Battle/        BattlePage・BattlePresenter
  Views/       BattlePageの部品(CommandView・SelfInfoView・HpGaugeView等)
  Selection/ Switch/ Forfeit/ OpponentSwitching/ Result/
```

- Pageは機能名の直下、Modalは機能名の下にModalごとのフォルダを切る(namespaceは機能名のまま)。
  Pageの部品が多ければ`Views/`にまとめる
- 他の機能と共用する部品は、最初に作った機能のフォルダに置いたまま参照する
- prefab側はModalも含めて`Views/{機能名}/`の直下に置く(C#側だけModalのフォルダを切るのは、prefabは1画面1ファイル、
  C#は1画面4〜5ファイルになるため)
- 採用しなかった案: 1画面ごとにフォルダを切る(`Party/PartyEdit/PartyEditPage.cs`)。1機能1Pageのため、
  機能名のフォルダの直下が空になるだけだった

### 6.8 バトル画面

`BattlePresenter`が`IBattleConnection`を持つ唯一のクラスで、Viewは`IBattleConnection`や
`Atlas.BattleCore`の型を知らない。接続の型(`IBattleConnection`とPayload)は
[IBattleConnection.cs](../Client/AtlasUnityProject/Assets/Scripts/Domain/IBattleConnection.cs)を正とし、
BattleServerとの通信契約([7.3](#73-通信契約))と対になる。

**流れ**

1. Battleシーンで`BattlePage`を開き、画面を先に出してからBattleServerに参加する(`JoinAsync`)。
   参加に失敗したらHomeへ戻る
2. `OnSelectionStart`で選出画面(`SelectionModal`)を開く
3. 「けってい」で`SubmitSelectionAsync`を送り、「相手の 選出を 待っています…」を出して選出画面のまま待つ。
   送信に失敗したら、選んだ内容を残したまま押し直せるようにする。選ばずに閉じられたら開き直す
4. `OnMatchStart`で選出画面を閉じ、対戦の画面を初期化する
5. ターンごとに行動を送り、`OnTurnResult`を演出してから次の入力を受け付ける
6. `OnBattleEnd`で結果のModalを出し、Homeへ戻る

**選出画面**(`SelectionModal` / `SelectionPresenter`)

- 上に残り時間、左に自分のパーティ(タップで選出に入れる/外す、選んだ順番1〜3を表示)、中央に最後にタップした
  パチモンの詳細(パーティ編成と同じ`PachimonInfoView`)、右に相手のパーティ、右下に「けってい」
- 「けってい」は、選出数(パーティが3体未満ならその体数)を選び終えていて、残り時間があるときだけ押せる
- 詳細の技は`OnSelectionStart`に含まれないため、手元の所持データ(`IPachimonMoveMappingService`)から出す
  (選出中はパーティ編成・技の付け替えができないため、BattleServerが使う技と食い違わない)
- 残り時間はクライアントで1秒ずつ減らして表示するだけで、時間切れの判定はBattleServerが行う
- `SelectionPresenter`は選んだ内容を`OnConfirmed`で流すだけで、送信は`BattlePresenter`が行う。
  自分からは閉じず、`BattlePresenter`が対戦の開始・決着で閉じる

**状態の持ち方**

- Payloadは全体のスナップショットではなくターンごとの差分なので、`BattlePresenter`が自分と相手の選出各枠の
  状態(種族・HP%・瀕死・場の枠)をローカルに持って更新する。`OnMatchStart`で初期化し(再接続時の再送もこれで復元)、
  `OnTurnResult`の各行動で更新する
- 相手の枠は、場に出て公開されるまで種族が分からない。一度公開された枠に交代で戻ったときは`RevealedPachimon`が
  送られないため、枠ごとに覚えておき`NewActiveIndex`で引き直す
- 技の表示・送信は、手元の所持データではなく`OnMatchStart`の`SelfMoves`(BattleServerが判定に使う技)を使う
- 残りPPは`SelfMoves`の値から、自分が技を使うたびにローカルで減らす(命中・外れに関わらず1減る)。
  PPが0の技のボタンは押せなくする(BattleCoreはPP0の技を受け取ると例外にするため、送らせない)

**入力と演出**

- 行動を送るとコマンドを隠し、「相手の 行動を 待っています…」を出す。ターン結果が届いたら行動順に1文ずつ
  メッセージを流し、HP・場のパチモンの表示もその文に合わせて更新する。流し終えてからコマンドを戻す
  - 1文は1.2秒で自動的に送る。タップで早送り
  - 技: 「(相手の)Xの 技名!」→ 技の演出とHPゲージ → 外れ/急所 → 効果(超ばつぐん・ばつぐん・いまひとつ・超いまひとつ・効果なし)→ 倒れた
  - 交代: 「ゆけっ! X!」/「相手は Xを くりだした!」
  - 何もしなかった: 「Xは 行動できなかった!」。強制交代ターンで待っていた側と、同じターンに先に倒れた側は出さない
  - 文言は`BattleMessageBuilder`にまとめる
- 演出中に次のターン結果や決着が届いても割り込ませず、届いた順に後ろへ並べる
- HPゲージ(`HpGaugeView`): 技でHPが変わったときは少しずつ減らし(LitMotion、ゲージ全体で1秒・最短0.25秒)、
  減り終わってから次の文へ進む。交代で替わったときは即時に切り替える。残りHPで色を変える(半分より多い=緑、
  半分以下=黄、2割以下=赤、`UiPalette`で管理)
- 技の演出(`BattleStageView`): 場のパチモンをサムネイルで表し、それを動かす。サムネイルが読み込めるまでと
  相手の未公開のパチモンは、主タイプの色の円(`PachimonTypePalette`で管理。未公開は灰色)を出す
- サムネイル(パーティ・スカウトの一覧、パーティ枠、対戦の場): Presenterはマスタの`pachimon_id`をDTOに入れるだけで、
  View(`PachimonThumbnailView`・`PachimonFigureView`)が`IAssetLoader`をInjectして`AddressDefinition.GetThumb(id)`で
  読み込む。読み込めるまではプレースホルダを出し、次の表示・破棄のときに前のものを解放する。一覧のセルは
  `IObjectResolver.Instantiate`で複製する(複製したセルはInjectされないため)
  - 物理技は攻撃側が相手へ体当たりし、特殊技は攻撃側から相手へ技のタイプの色のビームを伸ばす。
    相手に届いた瞬間から、攻撃側の戻り・攻撃を受けた側の反応・HPゲージの減少を同時に流す
  - 攻撃を受けた側は揺れながら点滅する。揺れの大きさは相性(超いまひとつ<いまひとつ<等倍<ばつぐん<超ばつぐん)
    で変え、急所は1.5倍。ばつぐん・超ばつぐん・急所のときは画面全体も揺らす。外れ・効果なしは反応を出さない
  - 倒れたときは沈みながら消え、繰り出したときは弾むように現れる
- 交代: 交代ボタン→`SwitchSelectModal`で選出3体から選ぶ。場に出ているパチモンと瀕死のパチモンは選べない
  (BattleCoreは場のパチモン自身への交代を検証しないため、UIで防ぐ)
- 強制交代: 自分が対象なら同じModalを「もどる」無しで出す(生存が1体でも自動では選ばない)。相手が対象なら
  `OpponentSwitchingModal`(「相手がパチモンを選んでいます」)を出して入力を止め、次のターン結果か決着で閉じる
- 交代先を選んでいる間にターン結果(時間切れのスキップ等)や決着が届いたら、Modalを閉じる
- 投了は確認のModal(`ForfeitConfirmModal`)を経て`ForfeitAsync`。決着は`OnBattleEnd`で届くので、
  結果の表示は投了と共通
- Modalは演出を流し終えてから出す(強制交代・相手の交代待ち・結果)
- 対象外: 送った行動の取り消し、対戦中のチャット・リアクション、観戦

### 6.9 通信エラーダイアログ

通信のエラーの扱いはgRPC基盤の`ApiCore`と`DefaultCommonErrorHandler`が決め、Atlasはダイアログの見た目(`ErrorDialog`)と
エラーの後の画面遷移(`ErrorNavigator`)、文言(`ErrorMessages`)を持つ。

- エラーはdomainとreasonの組(`ErrorCode`)で見分け、既定のaction(`Shared/errors.toml`・基盤の予約エラー)でボタンが決まる。
  OK(留まる)/ 再試行・タイトルへ / ホームへ / タイトルへ / ストアへ。再試行は同じRequestIDで送り直すので、サーバーで処理が
  終わっていても二重には実行されない
- `ErrorDialog`はBootstrapシーンの最前面のCanvas(`Assets/Prefabs/ErrorDialog.prefab`)に置く。USNのModalにしないのは、
  シーンの切り替え中やシーンのスコープが無いときにも出せるようにするため
- 「タイトルへ」: `ApiCore`がセッション(トークン・順番待ち)を終わらせた後、`ErrorNavigator`が手元の所持データを捨て
  (`IPlayerSession.SignOut`)、Homeシーンを読み直す(サインインしていないのでタイトルから始まる)。「ホームへ」はHomeシーンを
  読み直すだけ。ストアはまだ無いので、ログを出すだけ
- 文言は`ErrorMessages`の表で引き、知らないエラーは「予期しないエラー」として出す。メンテナンスは終了予定を端末の時刻で添える
- 対戦(MagicOnion)の参加失敗・切断は対象外で、`BattlePresenter`が扱う
- まだ無いもの: 通信中のLoading(基盤の入力ブロックのフックを使う)、ストアのページ

決めること: Loadingをすべての通信で自動にするか、捕まえ漏れの例外の通知。

---

## 7. バトルサーバー

### 7.1 構成

| プロジェクト | 場所 | 内容 |
|---|---|---|
| `BattleServer` | `BattleServer/` | ASP.NET Core + MagicOnion。Kestrelは開発中HTTP/2のみ・非TLS |
| `Atlas.BattleCore` | ソースは`Shared/BattleCore/Runtime/`、csprojは`BattleServer/BattleCore/` | ダメージ計算・ターン処理。Clientと同じソース |
| `Atlas.BattleContracts` | ソースは`Shared/BattleContracts/Runtime/`、csprojは`BattleServer/BattleContracts/` | 通信契約(Hub・Receiver・Payload)。Clientと同じソース |
| `Atlas.MasterData` | `BattleServer/MasterData/` | マスターデータの生成物([5.1](#51-生成と配布)) |
| `BattleBot` | `BattleServer/BattleBot/` | 開発用の対戦相手ボット([7.7](#77-テストボット)) |
| テスト | `BattleServer/Tests/` | xUnit |

- 公式のGetting Started・ChatAppサンプルと同じ最小構成(ASP.NET Core Empty + `MagicOnion.Server`)。
  専用のテンプレートは無い
- Docker化はしない(APIサーバーと同じく、Windowsでのビルドの速さとデバッグのしやすさを優先。デプロイの方式を
  決めるときに検討する)

`BattleServer/`の中の構成:

| 場所 | 役割 |
|---|---|
| `Hubs/BattleHub.cs` | StreamingHub。1接続1インスタンスで、「どの対戦のどちら側か」だけを持ち、処理は`BattleCoordinator`に委ねる |
| `Battle/BattleCoordinator.cs` | 全対戦の状態(`matchId`→`BattleSession`)と進行。タイマーはHubの外で発火するため、進行はこのシングルトンに置く |
| `Battle/BattleSession.cs` | 1対戦の状態(フェーズ・参加者・パーティ・選出・盤面・タイマー・行動ログ)。`Gate`(セマフォ)を取ってから読み書きする |
| `Battle/BattleTimingOptions.cs` | 制限時間類(テストで差し替える) |
| `Battle/IParticipantDataSource.cs` | パーティ・選出個体の取得とタイプ相性の抽象(実装は`Internal/ApiParticipantDataSource.cs`) |
| `Battle/LoadoutBuilder.cs` | 所持データ + マスタ → BattleCoreの型(`ParticipantStats`/`MoveData`/`ITypeChart`) |
| `Battle/MasterDatabaseFactory.cs` | 起動時のマスタの読み込み |
| `Auth/BattleTokenValidator.cs` | `battle_token`の検証 |
| `Internal/BattleResultReporter.cs` | 結果報告(再送付き) |

**共有ソースの持ち方**

- `Atlas.BattleCore`と`Atlas.BattleContracts`は、Unityのローカルパッケージ(`Shared/`)として置き、
  BattleServerは同じソースをコンパイルするだけのcsprojを`BattleServer/`側に置く
- csprojをUnityのパッケージの外に置くのは、パッケージ内に置くとビルド成果物(`bin`/`obj`)のDLLをUnityが
  取り込み、同名アセンブリの重複(CS1704)になるため
- Unityで使えない書き方をサーバー側のビルドで見つけられるよう、`LangVersion`をUnityと同じ`9.0`にし、
  `ImplicitUsings`は無効にする
- Unity側はDLLにせずソースのまま使う(変更のたびにDLLを作る手間と、テスト・デバッグのしづらさを避けるため)

### 7.2 Atlas.BattleCore

ClientのMock対戦とBattleServerの両方で、同じダメージ計算・ターン処理を使うための共通モジュール。

- `UnityEngine`・MagicOnion・通信の型・マスターデータの生成型のどれにも依存しない。自前の最小の型
  (`ElementType`、`MoveCategory`、`ParticipantStats`、`MoveData`等)だけを持つ
- マスタ + 所持データ(努力値)からBattleCoreの型への変換は、呼び出し側が持つ(BattleServerは`LoadoutBuilder`、
  ClientのMockは`TestPartyFactory`)。レベル50の固定値と能力の計算式もそちら側にある(BattleServerの
  `LoadoutBuilder`と、Clientの`PachimonStatCalculator`(画面表示・Mock用、努力値なし)の2か所)
- 乱数は`IRandomSource`、タイプ相性は`ITypeChart`で外から渡す(テストで固定値に差し替えるため)
- 入口は`BattleEngine.ProcessTurn(state, player1の行動, player2の行動, typeChart, random)`だけ。行動が無い
  (時間切れ等)側は`null`を渡す

**内部の構造**

CEDEC2026のポケモン・バトルシステムの講演にある考え方を採る。処理を再利用できる単位(Section)の階層に分け、
技ごとの個別の効果は、Sectionが発火するイベントに反応するハンドラとして足す(Sectionを変えずに追加できるようにする)。

```
ターン処理
├─ 強制交代の確認         前のターンに瀕死になった側に交代を求める
├─ 行動順の決定           交代 > 技。技どうしはすばやさ、同じならランダム
└─ 行動の実行(行動順に)
    ├─ 交代
    └─ 技
        ├─ 命中判定
        ├─ ダメージ(攻撃力・防御力の決定 → 算出)
        ├─ 瀕死の確認       選出が全員瀕死なら決着
        └─ 技の後処理       「技が当たった」イベント(MoveHitEvent)を発火。現状ハンドラは無い
```

天候・特性・持ち物は恒久的に対象外のため、それらのイベントは作らない。拡張点は「技が当たった後」の1か所
(将来の追加効果用)だけ。

**計算**

```
命中:     1〜100の乱数 <= 技の命中率
ダメージ: floor(floor(floor(2 * 50 / 5 + 2) * 威力 * A / D) / 50 + 2) * タイプ一致 * タイプ相性 * 急所 * 乱数
能力:     HP     = floor((2 * 種族値 + floor(努力値 / 4)) * 50 / 100) + 50 + 10
          HP以外 = floor((2 * 種族値 + floor(努力値 / 4)) * 50 / 100) + 5
```

- A/Dは、物理技ならこうげき/ぼうぎょ、特殊技ならとくこう/とくぼう
- タイプ一致1.5倍、タイプ相性0/0.5/1/2倍(2タイプなら2回かけ合わせる)、急所は1/16で1.5倍、乱数は0.85〜1.00
- 性格の補正は無い(マスタに性格が無いため)
- 能力の式の`floor(努力値 / 4)`は本家の努力値(最大252)を前提にしたもの。仕様概要書のステータスポイント
  (64ポイント)への合わせ方は未確定([8](#8-未確定の論点))
- PP: 技を選んだ時点で1減る(外れても減る)。PPが0の技を渡されると`ArgumentException`にする
  (呼び出し側が送らせない前提。Clientは押せなくし、BattleServerは受け付けない)
- 状態技は現状効果なし

### 7.3 通信契約

`Shared/BattleContracts/Runtime/`の[IBattleHub.cs](../Shared/BattleContracts/Runtime/IBattleHub.cs)と
[BattlePayloads.cs](../Shared/BattleContracts/Runtime/BattlePayloads.cs)を正とする(MessagePackでシリアライズ)。
Client側の`IBattleConnection`はこれと対になる形で、`RealtimeBattleConnection`が型を詰め替えるだけ。

| Client → Server(`IBattleHub`) | 内容 |
|---|---|
| `JoinAsync(battleToken, matchId)` | 参加(再接続も同じ)。結果は`Success`/`InvalidToken`/`MatchNotFound`/`AlreadyJoined` |
| `SubmitSelectionAsync(playerPachimonIds)` | 選出(1〜3体、選んだ順) |
| `SubmitMoveAsync(move)` | 技を使う |
| `SwitchAsync(partySlot)` | 交代(選出の中の位置、0始まり) |
| `ForfeitAsync()` | 降参 |

| Server → Client(`IBattleHubReceiver`) | 内容 |
|---|---|
| `OnSelectionStart` | 選出の開始。自分のパーティ(個体ID・種族)、相手のパーティ(種族だけ)、選出数、残り秒数、送信済みか |
| `OnMatchStart` | 対戦の開始(再接続時の盤面の再送も兼ねる)。自分・相手の選出各枠、場の枠、ターンの制限時間、自分の技と残りPP |
| `OnTurnResult` | ターンの結果。行動順の各行動と、次のターンで強制交代が要るプレイヤー |
| `OnBattleEnd` | 決着。勝者のID(勝者なしは空文字)と理由(`AllFainted`/`Forfeit`/`DisconnectTimeout`) |
| `OnOpponentDisconnected` / `OnOpponentReconnected` | 相手の切断・再接続 |

**情報の見せ方**(判定はサーバーだけが持ち、クライアントには結果だけを送る)

- HPは自分・相手とも0〜100の整数の%で送る(生きていれば最低1)。実数やダメージ量を送ると計算式の定数を
  逆算されやすいため
- 相手の選出は、場に出るまで種族を送らない(枠の数と瀕死の数は分かる)。一度公開した枠は公開のまま。
  初めて場に出た枠は、交代の結果に`RevealedPachimon`として入れる
- 技は自分の分だけ送る。Clientは手元の所持データではなくこれを表示・送信し、サーバーが判定に使う技と常に一致させる
- 選出画面で見せる相手のパーティは種族だけ(個体ID・技・努力値は送らない)

### 7.4 対戦の進行

```
WaitingForJoin ──(2人そろう)──→ Selecting ──(両者が選出)──→ InProgress ──(決着)──→ Finished
```

**参加**

- `battle_token`を検証し、`matchId`がトークンと一致するか確かめる。先に参加した側が`player1`
- 新規の参加では、APIサーバーの`/internal/battle/party`でその人のパーティを取得して保持する(対戦中に編成を
  変えても影響させない)。取得できなければ`Unavailable`で失敗させ、参加は未確定のまま残す
- 相手が来ない場合も、切断と同じ猶予(60秒)で打ち切る
- 2人目がそろったら`Selecting`にし、選出の制限時間を張って、それぞれに`OnSelectionStart`を送る

**選出**

- 検証: 1〜3体、空のIDや重複が無い、すべて参加時のパーティの中の個体(違反は`InvalidArgument`)
- APIサーバーの`/internal/battle/loadouts`で選出個体の所持データを取得し、マスタと合わせてBattleCoreの型にする。
  所持していなければ`InvalidArgument`、取得できなければ`Unavailable`(選出は未確定のまま残り、送り直せる)。
  所持データの取得を選出時に1回だけにするのは、対戦中に編成・技を変えても影響させないため
- 2回目以降の送信は無視する(再接続後に送り直した場合など)
- 両者がそろったら`InProgress`にし、それぞれに`OnMatchStart`を送る

**ターン**

- 行動すべき側の行動がそろったら解決し、全員に`OnTurnResult`を送る。制限時間(30秒)が切れたら、送らなかった側は
  何もしない扱いで解決する
- 強制交代ターン: 前のターンで瀕死になった側の交代だけを受け付け、相手の行動は`FailedPrecondition`で拒否する。
  交代が届いた時点で解決する(相手は`null`の行動としてBattleCoreに渡すため、結果に相手のSkipが入り、ターン番号も進む)。
  時間切れは倒れた側の負けで、スキップにはしない(待っている相手の番が来ないため)
- 1ターンで両者が同時に瀕死になることは現在のルールでは無いが、`PlayersRequiringForcedSwitch`の全員の交代が
  そろった時点で解決する形にしてある
- 以前は強制交代を次のターンの一部として扱い、相手も同じターンに技を送れた。倒れた側だけが不利になるうえ、
  相手が交代を待たずにコマンドを選べてしまうため、本家と同じ方式に変えた

**決着と時間切れ**

| 状況 | 結果 |
|---|---|
| 選出が全員瀕死 | 相手の勝ち(`AllFainted`) |
| 降参 | 相手の勝ち(`Forfeit`) |
| 選出の制限時間(2分)に選ばなかった | 相手の勝ち(`Forfeit`)。両者なら勝者なし |
| 強制交代の時間切れ(30秒) | 相手の勝ち(`Forfeit`) |
| 時間切れで何もしなかったターンが3回続いた(強制交代ターンは数えない) | 相手の勝ち(`Forfeit`)。両者同時なら勝者なし |
| 切断・未参加が60秒続いた | 相手の勝ち(`DisconnectTimeout`)。対戦開始前に両者とも不在なら勝者なし |

- 選出の時間切れ・放置に専用の理由は足さず`Forfeit`で表す(`BattleEndReason`はUnityと共有するBattleCoreの型のため、
  値を増やさない)
- 決着したら全員に`OnBattleEnd`を送り、APIサーバーへ結果を報告する。終わった対戦は60秒後にメモリから消す

**切断と再接続**

- 切断は`OnDisconnected`で検知し、相手に`OnOpponentDisconnected`を送って猶予タイマー(60秒)を張る。
  猶予中は両者とも行動できず、ターンのタイマーも止める
- 猶予中に同じトークンで`JoinAsync`し直せば再開する。トークンの有効期限(30秒)は猶予より短いため、
  再接続に限り期限切れ(署名は正当)のトークンを受け付ける
- 専用の再同期のメソッドは設けず、再接続した人にだけ現在の状態を送り直す(選出中なら`OnSelectionStart`、
  対戦中なら`OnMatchStart`)。Clientは受け取った内容で初期化するため、そのまま復元になる。ターンのタイマーは
  30秒からやり直す
- 猶予中に戻らなければ、切断した側の負け
- Clientの再接続(切断を検知して参加し直す処理)は未実装(progress.mdのC-3)

### 7.5 APIサーバーとの連携

- 内部APIは`X-Internal-Secret`付きのREST(`/internal/battle/party`・`loadouts`・`result`、[3.8](#38-内部apibattleserver用))。
  シークレット(`INTERNAL_API_SECRET`)が無いと対戦を始められないため、未設定なら起動しない
- 結果報告(`BattleResultReporter`): 通信エラー・5xxは1秒・5秒・15秒の間隔で再送し、409(記録済み)は成功扱い、
  それ以外の4xxは再送しない。再送し切っても失敗した場合の保存はしていない(APIサーバーの後始末で`aborted`になる)。
  報告の完了は待たずに進める
- 報告するターンのログには、クライアントに送らないHPの実数やダメージ量も含める
- 採用しなかった案:
  - BattleServerがMySQLを直接読む: DBへのアクセスをAPIサーバーにまとめる方針から外れるため
  - マッチ成立時に`battle_token`へパーティ全員分の所持データを入れる: 使わない個体の分までトークンに載り、
    トークンの役割(参加資格の証明)とも混ざるため

### 7.6 マスターデータ

起動時に`MasterData/masterdata.bytes`(ビルド時に出力先へコピー、パスは`MasterData:Path`で変更可)を1回読み込み、
`MemoryDatabase`をシングルトンで持つ。読み込めなければ起動しない。DBやAPIサーバーには問い合わせない。

### 7.7 テスト・ボット

- `Atlas.BattleCore`のロジックは、UnityのEditModeテスト(`Shared/BattleCore/Tests/`)で、乱数を固定して確かめる
- BattleServerはxUnit(`dotnet test BattleServer.slnx`)。インプロセスで起動したサーバーに2つのMagicOnionクライアントで
  接続する結合テスト(`BattleHubTests`)、`LoadoutBuilderTests`、`ApiParticipantDataSourceTests`、
  `MasterDatabaseFactoryTests`、`BattleResultReporterTests`。結合テストではAPIサーバーを固定値のデータ源
  (`DummyParticipantDataSource`)に差し替え、制限時間を短くする
- 対戦相手ボット(`BattleBot`、`make bot`): gRPC(Unityと同じ順番でデバイス登録〜サインイン)とMagicOnionで自動対戦する。
  Unity以外の.NETのクライアントなので基盤の`ApiCore`は使えず、`.proto`から`Grpc.Tools`で生成した素のクライアントに
  `authorization`・`x-request-id`のヘッダーを自分で付ける(再認証・再送はしない)。基盤のデバイス登録の`.proto`は基盤のRustの
  クレートにしか無いので、ビルドのときに基盤のタグの版を取ってくる。
  起動のたびに新しいプレイヤーを作り、`DebugService/RandomizeParty`でパーティをランダムにする。選出はパーティから
  ランダム、技は使えるものからランダム、強制交代は生存している先頭の控え。`--loop`で対戦のたびにマッチングに
  並び直し、`--count 2`でボット同士を対戦させる
- Clientを実際に操作する結合テスト(パーティ編成→マッチング→対戦→結果)に[Anjin](https://github.com/DeNA/Anjin)を使う案がある
  (未着手)。Anjinは最後まで例外なく動くかの確認に絞り、数値の検証はEditModeテストで行う
- ClientのMock対戦(`MockBattleConnection`)の相手の簡易AIも同じ方針(技はランダム、自分から交代しない、
  強制交代は先頭の控え)。プレイヤーの強制交代ターンでは行動せず、自分の強制交代はプレイヤーを待たずに処理する

---

## 8. 未確定の論点

| 論点 | 関係する節 |
|---|---|
| ステータスポイントの割り振り・育成チケット・ショップのAPI設計。決まっていること: 技の付け替えと割り振りの変更はそれぞれチケット1枚、64ポイントを6能力に上限なしで割り振り、能力の値にそのまま足す(今の`floor(努力値 / 4)`から計算式を変える)、ショップは販売期間・購入履歴なし | [4.4](#44-所持データ)、[5.2](#52-テーブル)、[7.2](#72-atlasbattlecore) |
| 通信中のLoadingをすべての通信で自動にするか | [6.9](#69-通信エラーダイアログ) |
| Clientの再接続と、強制交代ターンの途中で再接続したときの状態の伝え方(`OnMatchStart`に強制交代の状態が無い) | [7.4](#74-対戦の進行) |
| 報酬額(初期300・勝利50)とスカウト・育成チケットの値段のバランス | [2.5](#25-所持リソースアイテム) |
| 対戦結果(勝敗・報酬)をHomeへ戻ったときにどう見せるか | [6.8](#68-バトル画面) |
| マスターデータのClientへの配布をCDN経由にするか | [5.3](#53-運用) |
| 能力の計算式がClient(`PachimonStatCalculator`)とBattleServer(`LoadoutBuilder`)の2か所にある | [7.2](#72-atlasbattlecore) |
