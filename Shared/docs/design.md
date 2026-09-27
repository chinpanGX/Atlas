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
   │ REST (HTTP/JSON)              │ MagicOnion (gRPC / StreamingHub)
   ▼                               ▼
APIサーバー(Rust/axum)        バトルサーバー(C#/MagicOnion)
 認証・プレイヤー・スカウト・      対戦の進行のみ(状態はメモリ上)
 チャット・マッチング・DB書き込み        │
   ▲─────────── 内部API(REST) ──────────┘  パーティ・選出個体の取得、結果報告
   │
 MySQL(唯一のデータストア。書き込みはAPIサーバーだけが行う)
```

- サーバーは1台構成(ローカルでの動作を想定)。スケールアウトは考えない
- DBへの書き込みはAPIサーバーに一本化する。バトルサーバーはDBを持たず、必要なデータは内部APIで
  APIサーバーから取得し、結果も内部APIで報告する
- 対戦の判定はバトルサーバーが権威を持つ(クライアントは結果を受け取って表示するだけ)
- バトルサーバーはユーザーDBを持たない。参加資格は、APIサーバーがマッチ成立時に発行する短命JWT
  (`battle_token`)を共有シークレットで検証して確かめる

### 1.2 通信の一覧

| 経路 | 方式 | 認証 | 用途 |
|---|---|---|---|
| Client → APIサーバー | REST(JSON)。OpenAPIから生成したクライアント([3.1](#31-構成)) | `Authorization: Bearer <access_token>` | 認証・所持データ・スカウト・チャット・マッチング |
| Client ⇔ バトルサーバー | MagicOnion StreamingHub(HTTP/2) | `battle_token`(JWT) | 対戦(選出・行動・結果の受信) |
| バトルサーバー → APIサーバー | REST(JSON)。OpenAPIには載せない | `X-Internal-Secret` | パーティ・選出個体の取得、結果報告 |

### 1.3 リポジトリ構成

| パス | 内容 |
|---|---|
| `Client/AtlasUnityProject/` | Unityクライアント |
| `Server/` | APIサーバー(Rust/axum) |
| `BattleServer/` | バトルサーバー(ASP.NET Core + MagicOnion)。対戦相手ボット(`BattleBot/`)とテストを含む |
| `Shared/BattleCore/` | ダメージ計算・ターン処理(`Atlas.BattleCore`)。ClientとBattleServerで同じソースを使う |
| `Shared/BattleContracts/` | Client⇔BattleServerの通信契約(`Atlas.BattleContracts`) |
| `Shared/master-data/` | マスターデータのスキーマとCSV(正本) |
| `Shared/api/openapi.yaml` | APIサーバーのOpenAPI仕様書(生成物) |
| `master-data-pipeline/`(submodule) | マスターデータの生成ツール(自作) |
| `api-codegen/`(submodule) | OpenAPI → Unity向けDTO・通信クライアントの生成ツール(自作) |
| `Supplement/`(submodule) | Unity共通ユーティリティ(自作) |
| `UnityScreenNavigator/`(submodule) | 画面遷移ライブラリ(USN)のフォーク |

起動手順・環境変数は[DEVELOPMENT.md](../../DEVELOPMENT.md)を参照。

---

## 2. 共通の決まり

### 2.1 命名規則

- DB・Rust: `snake_case`。マスタは修飾語なし、プレイヤーの所持データは`player_`を付ける
  (`pachimon` / `player_pachimon`)
- 「ポケモン」にあたる語は`pachimon`で統一する(商標と混同しないための独自名)
- REST APIのJSON: `camelCase`(Rust側は`serde(rename_all = "camelCase")`で変換し、内部は`snake_case`のまま)
- C#(Client・BattleServer・通信契約): C#の規約どおり`PascalCase`

### 2.2 ID

プレイヤーの操作で生成されるデータのIDはULID(26文字、生成順に並ぶ)にし、DBには`CHAR(26)`の文字列で持つ。
Rustでは`ulid`クレートで生成する。所持データの「割当」(パーティの枠・覚えている技)も、行ごとにULIDを持つ
独立したエンティティとして扱う(`playerDiff`の`removed`でIDを指せるようにするため)。

マスタのIDは整数(`pachimon_id`は1001〜の4桁)。

### 2.3 nullableを使わない

APIのレスポンスには`nullable`を使わない。`api-codegen`が`nullable`に対応していないためで、
ツール側に対応を足すのではなく、APIとスキーマの側で表現を工夫する。

- 「無い」ことは、行が存在しないことで表す(パーティの未編成の枠は`player_party_slots`に行を作らない)
- 値が無い文字列は空文字にする(マッチング待ちの`matchId`、勝者なしの`winnerId`)
- 状態によって中身が無いフィールドは、使う側が別のフィールド(`status`等)で判定する

BattleServerとClientの間(MessagePack)は`nullable`を使ってよい(`api-codegen`を通らないため)。

### 2.4 所持データの差分(`playerDiff`)

プレイヤーの所持データ(アイテム・パチモン・覚えている技・パーティ)を変えるAPIは、レスポンスに
`playerDiff`を含める。クライアントは受け取った差分を手元のデータに反映する。レスポンスの形をAPIごとに
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
  4種別は常にすべて含め、変化が無い種別は両方とも空配列にする(`nullable`を避けるため)
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

| API | `playerDiff`の中身 |
|---|---|
| `POST /sign-in` | 全種別の全件(`removed`は常に空)。差分ではなく、手元のデータを置き換える全件として扱う |
| `POST /edit/party` | `partySlots`(新しい枠を`upserted`、解除した枠を`removed`) |
| `POST /edit/pachimon_moves` | `pachimonMoveMap`(付け替えた1件) |
| `POST /scout/rolls` | `items`(消費後のジェム) |
| `POST /scout/rolls/{rollId}/select` | `pachimon`(入手した1体)と`pachimonMoveMap`(その技) |
| `POST /debug/grant_gems` | `items`(付与後のジェム) |
| `POST /debug/randomize_party` | `pachimon`・`pachimonMoveMap`(新しく付与した6体)と`partySlots` |

### 2.5 所持リソース(アイテム)

ジェムのような数量で持つリソースは、`players`に専用の列を持たせず、マスタ`items`と所持数テーブル
`player_items`で管理する。所持数は絶対値で、増減はすべてサーバーが決める。現状のアイテムはジェム
(`item_id: 1`)だけ。

| きっかけ | 増減 | 処理する場所 |
|---|---|---|
| プレイヤー作成 | +300 | `POST /signup` |
| 対戦の勝利 | +50 | `POST /internal/battle/result` |
| 開発用の付与 | +450 | `POST /debug/grant_gems` |
| スカウトの紹介 | −(バナーの`cost_per_roll`) | `POST /scout/rolls` |

---

## 3. APIサーバー

### 3.1 構成

Rust/axum。ソースは`Server/src/`、詳しい開発手順は[Server/CLAUDE.md](../../Server/CLAUDE.md)。

| ディレクトリ・ファイル | 役割 |
|---|---|
| `api/` | リクエスト・レスポンスの型とハンドラ。`utoipa`でOpenAPIの注釈を付ける |
| `service/` | 業務処理とDBアクセス |
| `model/` | DBのテーブルに対応する構造体 |
| `master/` | マスターデータの型(生成物)と、起動時に読み込むキャッシュ(`MasterData`) |
| `extractor.rs` | 認証(`AuthenticatedDevice`: アクセストークン、`InternalService`: 内部APIのシークレット) |
| `error.rs` | `AppError` → HTTPステータスの変換 |
| `state.rs` | `AppState`(DB接続プール・マスタのキャッシュ・マッチングの待機列) |
| `routes.rs` | ルーティング |
| `openapi.rs` | OpenAPI仕様書(`ApiDoc`)。内部APIは載せない |

- エラー: `AppError`の`BadRequest`→400、`Unauthorized`→401、`NotFound`→404、`Conflict`→409、
  `InternalError`→500
- マスターデータは起動時にDBから全件読み込み、`Arc<MasterData>`としてメモリに持つ(更新にはサーバーの再起動が要る。
  [5.3](#53-運用))
- OpenAPI仕様書は`cargo run --bin export_openapi`で`Shared/api/openapi.yaml`に出力し、`api-codegen`で
  Unity向けのDTOと通信クライアント(`Client/.../Infrastructure/Api/Generated/`)を生成する。
  API(ハンドラ・DTO)を変えたら生成し直す
- ログ: `tracing`でリクエスト・レスポンスの本文と実行したSQLを出す(`RUST_LOG`で切り替え)

### 3.2 認証

**デバイス認証(ゲスト)**

- 会員登録は無く、端末ごとのデバイスで認証する。クライアントが`secret_key`(ランダムな文字列)を作り、
  `POST /devices`で登録して`device_id`を受け取る。以後は`device_id`と`secret_key`で`POST /devices/authenticate`を
  呼び、アクセストークンを受け取る
- `secret_key`はArgon2でハッシュ化して保存する
- アクセストークンの有効期限は1時間。1つのデバイスにつき有効なトークンは常に1つで、再認証すると
  古いトークンは無効になる
- リフレッシュトークンは無く、`POST /devices/authenticate`の再実行が更新を兼ねる(クライアントの更新方法は
  [6.6](#66-通信と所持データ))
- デバイス(端末の識別)とプレイヤー(ゲームデータの持ち主)は分ける。プレイヤーは`POST /signup`で作る

**内部API**

- BattleServerからの呼び出しは`X-Internal-Secret`ヘッダーのシークレットで認証する(定数時間で比較し、
  不一致・欠落は401)。共有値は両サーバーの環境変数`INTERNAL_API_SECRET`

**`battle_token`**

- マッチ成立時にAPIサーバーが発行するHS256のJWT。claimsは`match_id`・`player_id`・`exp`で、有効期限は30秒
- 共有シークレットは環境変数`BATTLE_TOKEN_SECRET`(32バイト未満ならBattleServerは起動しない)
- BattleServerは検証の時刻のずれの許容を小さくする(既定の許容が30秒より大きいと実質の有効期限が延びるため)
- 再接続のときに限り、期限切れ(署名は正当)のトークンを受け付ける([7.4](#74-対戦の進行))

### 3.3 API一覧

| メソッド | パス | 内容 | 認証 |
|---|---|---|---|
| POST | `/devices` | デバイスの登録 | 不要 |
| POST | `/devices/authenticate` | デバイス認証(アクセストークンの発行) | 不要 |
| GET | `/auth/verify` | アクセストークンの検証 | 要 |
| POST | `/signup` | プレイヤーの作成(初期データの付与) | 要 |
| POST | `/sign-in` | サインイン(所持データの全件取得) | 要 |
| POST | `/edit/party` | パーティ編成 | 要 |
| POST | `/edit/pachimon_moves` | 技の付け替え | 要 |
| POST | `/chat/send` | チャットの送信 | 要 |
| GET | `/chat/poll` | チャットの取得 | 要 |
| GET | `/scout/banners` | 開催中のスカウト一覧 | 要 |
| POST | `/scout/rolls` | 紹介を受ける(ジェム消費・候補10体) | 要 |
| POST | `/scout/rolls/{rollId}/select` | 候補から1体を選んで入手 | 要 |
| POST | `/battle/queue` | マッチングの待機列に入る | 要 |
| DELETE | `/battle/queue` | 待機列から抜ける | 要 |
| GET | `/battle/queue/status` | マッチ成立の確認(ポーリング) | 要 |
| POST | `/debug/grant_gems` | 開発用: ジェムを付与 | 要 |
| POST | `/debug/randomize_party` | 開発用: パーティをランダムに組み直す | 要 |
| POST | `/internal/battle/party` | 内部: 参加者のパーティ取得 | 内部 |
| POST | `/internal/battle/loadouts` | 内部: 選出個体の所持データ取得 | 内部 |
| POST | `/internal/battle/result` | 内部: 対戦結果の記録 | 内部 |

- パスは動詞をbodyに寄せたRPC寄りの命名にしている(`/edit/party`、`/chat/send`)。識別子もなるべくパスではなく
  bodyに置く(仕様変更に強くするため。`/scout/rolls/{rollId}/select`は例外)
- 以下の各APIでは、`playerDiff`の中身は[2.4](#24-所持データの差分playerdiff)の表を参照し、JSONの全体は載せない

### 3.4 デバイス・プレイヤー

**`POST /devices`** — `{ "secretKey" }` → `{ "deviceId" }`

**`POST /devices/authenticate`** — `{ "deviceId", "secretKey" }` → `{ "accessToken", "expiresIn" }`(秒)。
`playerDiff`は返さない(トークン発行専用)。

**`GET /auth/verify`** — 有効なら200、無効(期限切れ・存在しない)なら401。

**`POST /signup`** — `{ "nickname" }` → 200(本文なし)

- 1つのデバイスにプレイヤーは1人(2人目は409)
- プレイヤーの作成と同じトランザクションで次を付与する
  - スターターパーティ: マスタ`starter_party_slots`の6体をそのまま複製し、初期技込みの`player_pachimon`と
    `player_party_slots`を作る(全員共通の固定編成)
  - ジェム300(`player_items`)
- 作成したデータは返さない。クライアントは続けて`POST /sign-in`を呼ぶ

**`POST /sign-in`** — 本文なし → `{ "playerId", "nickname", "playerDiff" }`

- デバイス認証の直後に毎回呼び、所持データの全件を返す
- プレイヤーが未作成なら404。クライアントは404のときだけ`POST /signup`を呼んでから、もう一度サインインする

### 3.5 パーティ編成・技の付け替え

**`POST /edit/party`** — `{ "partySlots": [ { "slot", "playerPachimonId" } ] }` → `{ "playerDiff" }`

- 全置き換え。既存の枠を全て削除し、指定された枠を新しいULIDで作り直す(リクエストに無い枠は解除される)
- 検証: 1〜6体(0体は400)、slotは1〜6で重複しない、同じ個体を2つの枠に入れない(400)、
  自分の所持個体であること(404)。同じ種族の別の個体は何体でもよい

**`POST /edit/pachimon_moves`** — `{ "playerPachimonId", "slot", "moveId" }` → `{ "playerDiff" }`

- 技を、その種族の技グループの候補(`move_group_moves`)から選んで入れ替える。候補に無い技・slotが1〜4の
  範囲外は400、他人の個体は404
- `INSERT ... ON DUPLICATE KEY UPDATE`で更新する。更新の対象に`player_pachimon_move_id`を含めないため、
  既にある枠の付け替えではIDが変わらない

### 3.6 チャット

全プレイヤー共通の1つのチャンネル(ルームの概念は無い)。

**`POST /chat/send`** — `{ "content" }` → `{ "messageId", "createdAt" }`

**`GET /chat/poll`** — `{ "messages": [ { "messageId", "playerId", "content", "createdAt" } ] }`。新しい順に50件。

### 3.7 スカウト

本家の「紹介(候補10体から1体を選ぶ)」をもとにした2段階の流れ。

**`GET /scout/banners`** — 開催中(`start_at <= 現在 <= end_at`)のバナー一覧
`{ "banners": [ { "bannerId", "name", "costPerRoll", "startAt", "endAt" } ] }`。排出率は返さない。

**`POST /scout/rolls`** — `{ "bannerId" }` → `{ "rollId", "candidates": [ { "index", "pachimonId", "rarity", "moves" } ], "playerDiff" }`

1. バナーが開催中か確かめる(存在しなければ404、開催期間外は400)
2. ジェムから`cost_per_roll`を減らす(足りなければ400)
3. 候補10体を1体ずつ抽選する
   1. `rate_table`の重みでレア度を決める
   2. そのレア度の種族から等確率で1体選ぶ
   3. 技はその種族の初期技(`move_group_moves.is_initial`)4つ
4. 候補10体を`scout_rolls`に保存する(未選択)
5. 以上を1つのトランザクションで行う

**`POST /scout/rolls/{rollId}/select`** — `{ "index" }` → `{ "playerDiff" }`

- 自分のロールであること(存在しない・他人のロールは404)、未選択であること(選択済みは409)、
  `index`が0〜9であること(範囲外は400)を確かめる
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

### 3.8 マッチング

**`POST /battle/queue`** — 本文なし → 200。パーティが0体なら400。既に待機中・成立済み(結果未取得)なら何もしない。

**`DELETE /battle/queue`** — 待機列から抜ける。いなくても200。

**`GET /battle/queue/status`** — `{ "status", "matchId", "battleServer", "battleToken" }`

- 待機中は`status: "waiting"`で他は空文字([2.3](#23-nullableを使わない))。成立時は`"matched"`で、
  接続先(`battleServer`、環境変数`BATTLE_SERVER_URL`)と`battle_token`を返す
- 成立の結果は1回取得したら消す

実装:

- 待機列はDBに置かず、`AppState`のメモリに`Mutex<VecDeque<player_id>>`(待機列)と
  `HashMap<player_id, MatchInfo>`(成立した結果)で持つ(1台構成のため)
- `POST /battle/queue`の中で同期的に組む。待っている人がいれば先頭と組み、両者分の`MatchInfo`を保存し、
  `battle_matches`に`in_progress`の行を作る(先に待っていた側が`player1`)
- 通知はポーリング(クライアントは1秒ごと)。1秒の遅れは体験に影響せず、WebSocketを入れる手間に見合わないため

### 3.9 内部API(BattleServer用)

`X-Internal-Secret`で認証し、OpenAPIには載せない(`api-codegen`がUnity向けのクライアントを作らないように)。

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
  付与を行う。対象の行を`FOR UPDATE`でロックし、二重の報告は409にする(ジェムを二重に付与しない)
- `player1`/`player2`はBattleServerに先に参加した側で、マッチ成立時の順と一致するとは限らない。
  IDで`battle_matches`の参加者と突き合わせて保存する
- `winnerId`が空文字なら勝者なし: `aborted`にし、ジェムは付与しない。相手が一度も参加しなかった場合、
  その側のIDは空文字・選出は空配列
- `actionData`・`resultData`は中身を解釈せずJSONのまま保存する(HPの実数など、クライアントに送らない値も含む)
- 報告済みかどうかは選出の列がNULLでないかで判定する。後始末で`aborted`になった対戦([3.10](#310-結果報告が届かない対戦の後始末))に
  後から届いた報告は受け付け、内容で上書きする
- 400: 参加者でない`winnerId`・`player1Id`/`player2Id`・ターンの`playerId`。404: 存在しない`matchId`

### 3.10 結果報告が届かない対戦の後始末

BattleServerの再起動(対戦状態はメモリにしか無い)、両者とも一度も接続しなかった対戦、報告の再送失敗により、
`in_progress`のまま残る対戦がある。APIサーバーは5分ごとに、開始から1時間以上経った`in_progress`の対戦を
`aborted`にする(`battle_service::spawn_stale_match_cleanup`)。1時間を超える正当な対戦も打ち切られ得るが、
後から届いた結果報告で上書きされるため実害は無い。

- 採用しなかった案: BattleServerの起動時に進行中の対戦をすべて打ち切るよう通知する。再起動のケースは早く片付くが、
  定期処理だけで全ケースを賄えるため見送った

### 3.11 開発用API

正式なゲーム内機能ではなく、動作確認のためのもの。OpenAPIには載せる(Unityのデバッグボタンから呼ぶため)。

- **`POST /debug/grant_gems`**: ジェム450(スカウト3回分)を付与する。回数の上限は無い
- **`POST /debug/randomize_party`**: マスタから重複なしで6体を選んで新しく付与し(技は候補からランダムに最大4つ)、
  パーティを組み直す。以前の所持個体は残る。対戦相手ボットが毎回違うパーティで対戦するために使う

---

## 4. DB

### 4.1 方針

- MySQL。スキーマは`Server/migrations/`のマイグレーション(`sqlx migrate`)で管理する
- プレイヤーの操作で生成する行の主キーはULID(`CHAR(26)`、[2.2](#22-id))。日時は`DATETIME(3)`
- `NULL`を許す列はなるべく作らない。「無い」ことは行が無いことで表す([2.3](#23-nullableを使わない))。
  例外は`scout_rolls.selected_index`・`battle_matches`の選出・勝者・日時(状態によって値が決まる列)
- マスタのテーブル(`pachimon`等)もDBに置く。定義は[5. マスターデータ](#5-マスターデータ)
- 所持データの「割当」(パーティの枠・覚えている技)は、元のテーブルの列ではなく専用のテーブルにする。
  未設定を行が無いことで表せ、`nullable`を使わずに済むため

### 4.2 テーブル一覧

| テーブル | 内容 | 主な書き込み元 |
|---|---|---|
| `devices` | 登録されたデバイス | `POST /devices` |
| `access_tokens` | デバイスごとのアクセストークン(1デバイス1行) | `POST /devices/authenticate` |
| `players` | プレイヤー(1デバイス1人) | `POST /signup` |
| `player_items` | 所持アイテム(ジェム等)の数 | サインアップ・スカウト・対戦の結果 |
| `player_pachimon` | 所持しているパチモンの個体 | サインアップ・スカウト |
| `player_pachimon_moves` | 個体が覚えている技(1個体4行まで) | サインアップ・スカウト・技の付け替え |
| `player_party_slots` | パーティの編成(1プレイヤー6行まで) | サインアップ・パーティ編成 |
| `messages` | チャットのメッセージ | `POST /chat/send` |
| `scout_banners` | スカウトのバナー(排出率・値段・期間) | `seed_scout_banners` |
| `scout_rolls` | スカウトの紹介と選択の記録 | スカウト |
| `battle_matches` | 対戦 | マッチ成立・結果報告 |
| `battle_turns` | 対戦の行動ログ | 結果報告 |

### 4.3 認証・プレイヤー

**devices**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `device_id` | CHAR(26) | PK | |
| `secret_key_hash` | VARCHAR(255) | NOT NULL | `secret_key`のArgon2ハッシュ |
| `created_at` | DATETIME(3) | NOT NULL | |

**access_tokens**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `device_id` | CHAR(26) | PK, FK → `devices` | 1デバイス1トークン。再認証はUPSERTで上書き |
| `access_token` | VARCHAR(64) | NOT NULL, UNIQUE | ランダムな文字列 |
| `expires_at` | DATETIME(3) | NOT NULL | 発行から1時間 |
| `created_at` | DATETIME(3) | NOT NULL | |

**players**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_id` | CHAR(26) | PK | |
| `device_id` | CHAR(26) | NOT NULL, UNIQUE, FK → `devices` | |
| `nickname` | VARCHAR(50) | NOT NULL | |
| `created_at` | DATETIME(3) | NOT NULL | |

ジェム等の数は持たない(`player_items`で持つ)。

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

### 4.5 チャット

**messages**

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `message_id` | CHAR(26) | PK | |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | 送信者 |
| `content` | TEXT | NOT NULL | |
| `created_at` | DATETIME(3) | NOT NULL, INDEX | 新しい順の取得のため |

### 4.6 スカウト

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

### 4.7 対戦

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
- マッチングの待機列はDBに置かない(APIサーバーのメモリ、[3.8](#38-マッチング))
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
| Client | C#の型(Models/Enums、MasterMemory + MessagePack)、ローダー、`masterdata.bytes`(暗号化) | 起動時にAddressablesから読み込む |
| バトルサーバー | Clientと同じC#の型・ローダー・`masterdata.bytes`(`BattleServer/MasterData/`) | 起動時にファイルから読み込み、`MemoryDatabase`をシングルトンで持つ。読み込めなければ起動しない |
| APIサーバー | Rustの型(`src/master/generated/`)とJSON | `seed_master_data`でDBへUPSERTし、サーバー起動時にDBから全件読み込む |

- ClientとBattleServerには同じ生成物をそれぞれにコピーする。共有パッケージは作らない
  (どちらも正本から毎回生成されるため食い違わない。BattleServerがClientのフォルダを参照するのは、
  サーバーからクライアントへの依存になるため採らない)
- C#の型・ローダーは1つのアセンブリにまとめる(Clientは`Atlas.MasterData.asmdef`、BattleServerは
  `Atlas.MasterData.csproj`)。MasterMemory・MessagePackのSource Generatorが同じアセンブリ内でしか働かないため
- 生成される型は「テーブル名のPascalCase + `Data`」(`pachimon` → `PachimonData`)で、`partial`。
  ゲームロジックはこの型を直接使わず、BattleCoreの型(`ParticipantStats`/`MoveData`)に変換して使う
- テーブルごとに配布先(`targets`)を決められる。サーバーだけで使うもの(`move_groups`・`starter_party_slots`)は
  Clientの`masterdata.bytes`に含めない(Clientは`pachimon.move_group_id`から`move_group_moves`を直接引くため、
  `move_groups`自体は要らない)
- パイプラインの型は`int`/`long`/`string`/`bool`/`enum`だけで、小数・`nullable`・複合主キーを持てない。
  スキーマはこの制約に合わせる(下記の各テーブル)

### 5.2 テーブル

本家ポケモンの対戦から、天候・フィールド・持ち物・特性・技の追加効果を外した構成。レベルアップで技を
覚える仕組みは無く、パチモンに「技グループ」を紐付ける。

**pachimon**(36件)

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

**move_group_moves**(技グループが含む技。現状は各グループ5件)

| 列 | 説明 |
|---|---|
| `unique_id` | PK。パイプラインが複合主キーに対応しないための代理キー |
| `group_id` | FK → `move_groups` |
| `move_id` | FK → `moves` |
| `is_initial` | 初期技か。各グループちょうど4件(APIサーバーのテストで確かめている) |

**moves**(64件)

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
| `item_id` | PK。`1`がジェム |
| `name` | |

**Enum**: `pachimon_type`(18タイプ + `NONE`)、`rarity`、`move_category`、`type_effectiveness`
(`Shared/master-data/schema/enums/`)。

### 5.3 運用

- APIサーバー: `cargo run --bin seed_master_data`でDBへUPSERTし、サーバーを再起動する
- Client・BattleServer: パイプラインで生成して配置し、再起動する(Clientは作り直し)
- 無停止での反映は、1台構成の規模では過剰なため入れない
- Clientへの配布をCDN経由にする案は保留(バージョン管理・差分検知を決めていない)
- スカウトのバナーはマスターデータではない([3.7](#37-スカウト))

---

## 6. クライアント

Unity(`Client/AtlasUnityProject/`)。コーディング規約は[Client/AtlasUnityProject/CLAUDE.md](../../Client/AtlasUnityProject/CLAUDE.md)。

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
| 共通ユーティリティ(ローダー、暗号化セーブ、`IMessageBroker`等) | Supplement | submodule(`file:`) |
| 画面をまたぐ通知 | ZeroMessenger(Supplementの`IMessageBroker`経由でだけ使う) | NuGetForUnity |
| マスターデータ | MasterMemory + MessagePack(本体とSource Generator) | NuGetForUnity |
| 対戦の通信 | MagicOnion.Client + YetAnotherHttpHandler + Grpc.Net.Client | OpenUPM + NuGetForUnity + UnityNuGet |
| REST通信 | `api-codegen`の生成物(UnityWebRequest + UniTask) | 生成コード |
| テスト | Unity Test Framework | Unity |

- MagicOnion・MasterMemory・MessagePackは、UPM版が薄いラッパーでSource Generatorを含まないため、
  NuGet版(本体 + Source Generator)をNuGetForUnityで入れている
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
  RootLifetimeScope: マスタの読み込み、サインイン、Connection・Repository・ServiceのDI登録
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
- Overlay(Modalより手前のレイヤー)はコンテナだけ用意してあり、使う画面はまだ無い([6.9](#69-通信エラーダイアログloading設計案))

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

| 抽象 | 置き場所 | 内容 | Mock/Realの切り替え |
|---|---|---|---|
| `IXxxConnection` | `Atlas.Application` | サーバーのAPIの呼び出し(`IDeviceConnection`/`IPlayerConnection`/`IScoutConnection`/`IDebugConnection`) | この単位で切り替える |
| `IXxxRepository` | `Atlas.Domain` | 手元の所持データの読み書き。`playerDiff`の種別ごとに1つ(`IItemRepository`/`IPachimonRepository`/`IPachimonMoveMapRepository`/`IPartyRepository`)。ほかに`IPlayerProfileRepository`・`IDeviceCredentialsRepository` | 切り替えない |
| `IXxxService` | `Atlas.Application` | Presenterが使う処理(`ISignInService`/`IPlayerAccountService`/`IItemFetchService`/`IPartyService`/`IPachimonService`/`IPachimonMoveMappingService`/`IMasterDataService`) | 切り替えない(実装は1つ) |

- Connectionは`api-codegen`が生成する`XxxApiClient`の単位(タグ)に合わせる。1つの大きなConnectionにしないのは、
  一部の機能だけ本物のサーバーにつなぐような切り替えをしやすくするため
- 対戦は、マッチング(REST)とバトル本体(MagicOnion)で生存期間が違うため分ける。
  `IBattleMatchmaker`(待機列への参加から成立までのポーリング)はRootのSingleton。`IBattleConnection`は
  対戦ごとの接続のため、`IBattleConnectionFactory`をSingletonにしてBattleシーンのスコープで生成する
- チャットのConnectionは、画面を作るときに足す

**`playerDiff`の適用**

- `playerDiff`を返すAPIを呼ぶConnectionの実装は、受け取った直後に`IPlayerDiffApplier.ApplyAsync`を呼ぶ。
  `PlayerDiffApplier`は種別ごとの`XxxDiffApplier`に振り分け、各`XxxDiffApplier`が`upserted`/`removed`を
  対応するRepositoryの`Upsert`/`Delete`に変換する。APIごとに反映処理を書かないため
- `XxxDiffApplier`は生成DTOを直接受け取るため`Atlas.Infrastructure.Api`に置く。Serviceは`playerDiff`を
  知らず、Connectionを呼ぶだけでRepositoryが最新になる
- 汎用のDiff型・汎用のRepositoryは作らず、種別ごとに`XxxDiffApplier`とRepositoryを1組ずつ足す
- Repositoryはメモリに持つだけでディスクには保存しない。サーバーのDBが正で、起動のたびにサインインで
  全件を取り直せるため
- 例外はデバイス情報(`device_id`・`secret_key`)で、`DeviceCredentialsRepository`がSupplementの
  暗号化セーブで端末に保存する

**サインインの流れ**

```
BootstrapEntryPoint
  └─ ISignInService.SignInAsync()
       ├─ IDeviceCredentialsRepository     端末に保存したデバイス情報(無ければ作る)
       ├─ IDeviceConnection.RegisterAsync  初回だけ
       ├─ AccessTokenRefresher             デバイス認証 → AccessTokenStore
       ├─ IPlayerConnection.SignInAsync    POST /sign-in(404ならサインアップしてから再度)
       │    └─ IPlayerDiffApplier          → 各Repository
       └─ IPlayerProfileRepository         プレイヤーID・ニックネーム
```

**アクセストークンの更新**(`AccessTokenRefresher`、トークンを書き込むのはここだけ)

- 要認証APIの呼び出しは`AccessTokenRefresher.SendAsync(() => client.XxxAsync(...))`で包む
- 送信前: 有効期限(受け取った時刻 + `expiresIn`)の残りが5分未満なら、先に再認証する。
  バックグラウンドから戻った直後もここで更新される
- 401が返ったら、再認証して1回だけ送り直す
- 再認証は同時に1つだけ(サーバーは再認証で古いトークンをすぐ無効にするため)。実行中の再認証があれば
  それを待ち、送信時のトークンが既に差し替わっていれば再認証せずに送り直す
- 採用しなかった案: 一定間隔のタイマーでの確認(送信前の確認で足り、バックグラウンド復帰にも対応できる)、
  毎回の送信前の認証・検証(Argon2の照合でサーバーの負荷が大きく、並行するリクエストのトークンも無効にしてしまう)

**Mock/Realの切り替え**

- `RootLifetimeScope`の`ConfigureAuthConnections`(virtual)に認証系のConnectionの登録を切り出し、
  本番は常にReal。テスト用の派生スコープ(`TestRootLifetimeScope`)がMockに差し替える
- 対戦(`IBattleMatchmaker`/`IBattleConnectionFactory`)は`ConfigureBattleConnections`(virtual)に切り出し、
  Bootstrapシーンの`RootLifetimeScope`のInspector(`Use Battle Server`)で切り替える。オンで実サーバー、
  オフで`MockBattleConnection`(BattleCoreをクライアント内で動かし、相手は簡易AI)
- スカウト・開発用のConnectionはMockを作らず、常にReal
- 採用しなかった案: Repositoryを切り替えの単位にする(当初は`IPlayerRepository`のMock/Realを用意していた)。
  サーバーが返すものが「取得結果」ではなく「手元のデータへの差分」になったため、手元のデータ(Repository)と
  通信(Connection)を分けた

### 6.7 アセンブリとフォルダ

| asmdef | 置くもの | 参照 |
|---|---|---|
| `Atlas.Domain` | Entity、`IXxxRepository`、`IBattleConnection`とその型 | UniTask |
| `Atlas.Application` | `IXxxService`、`IXxxConnection`、`IBattleMatchmaker`/`IBattleConnectionFactory`、戻り値の型、`BattleEntryStore`、`AddressDefinition` | Domain, MasterData, UniTask |
| `Atlas.Infrastructure` | Serviceの実装、`DeviceCredentialsRepository`、`MasterDataService` | Domain, Application, Infrastructure.Api, MasterData, Supplement |
| `Atlas.Infrastructure.Api` | `api-codegen`の生成物(`Generated/`)、Connectionの実装、`PlayerDiffApplier`/`XxxDiffApplier`、Repositoryの実装、`AccessTokenStore`/`AccessTokenRefresher`、`ApiBattleMatchmaker` | Domain, Application, UniTask |
| `Atlas.Infrastructure.Mock` | Mockの認証系Connection、`MockBattleConnection`/`MockBattleMatchmaker` | Domain, Application, MasterData, BattleCore |
| `Atlas.Infrastructure.Realtime` | `RealtimeBattleConnection`(MagicOnionのクライアント) | Domain, Application, BattleContracts, BattleCore, MagicOnion |
| `Atlas.Navigation` | `TransitionAwareSceneNavigator` | USN, UniTask |
| `Atlas.Presentation` | View・Presenter・ViewDto(画面単位の型だけ) | Domain, Application, MasterData, USN, VContainer, R3 |
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
[IBattleConnection.cs](../../Client/AtlasUnityProject/Assets/Scripts/Domain/IBattleConnection.cs)を正とし、
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
  - 技: 「(相手の)Xの 技名!」→ HPゲージ → 外れ/急所 → 効果(ばつぐん・いまひとつ・効果なし)→ 倒れた
  - 交代: 「ゆけっ! X!」/「相手は Xを くりだした!」
  - 何もしなかった: 「Xは 行動できなかった!」。強制交代ターンで待っていた側と、同じターンに先に倒れた側は出さない
  - 文言は`BattleMessageBuilder`にまとめる
- 演出中に次のターン結果や決着が届いても割り込ませず、届いた順に後ろへ並べる
- HPゲージ(`HpGaugeView`): 技でHPが変わったときは少しずつ減らし(LitMotion、ゲージ全体で1秒・最短0.25秒)、
  減り終わってから次の文へ進む。交代で替わったときは即時に切り替える。残りHPで色を変える(半分より多い=緑、
  半分以下=黄、2割以下=赤、`UiPalette`で管理)
- 交代: 交代ボタン→`SwitchSelectModal`で選出3体から選ぶ。場に出ているパチモンと瀕死のパチモンは選べない
  (BattleCoreは場のパチモン自身への交代を検証しないため、UIで防ぐ)
- 強制交代: 自分が対象なら同じModalを「もどる」無しで出す(生存が1体でも自動では選ばない)。相手が対象なら
  `OpponentSwitchingModal`(「相手がパチモンを選んでいます」)を出して入力を止め、次のターン結果か決着で閉じる
- 交代先を選んでいる間にターン結果(時間切れのスキップ等)や決着が届いたら、Modalを閉じる
- 投了は確認のModal(`ForfeitConfirmModal`)を経て`ForfeitAsync`。決着は`OnBattleEnd`で届くので、
  結果の表示は投了と共通
- Modalは演出を流し終えてから出す(強制交代・相手の交代待ち・結果)
- 対象外: 送った行動の取り消し、対戦中のチャット・リアクション、観戦

### 6.9 通信エラーダイアログ・Loading(設計案)

**未実装の設計案**(progress.mdのC-4・C-6)。APIサーバーのgRPC化([feature-grpc-foundation.md](proposals/feature-grpc-foundation.md))で
通信層を作り直すため、実装はそれと合わせて決め直す。

現状の問題:

- 画面を出す`IScreenNavigator`がシーン(Home/Battle)ごとにしか無く、起動時のサインイン中やシーンの切り替え中に
  出せる画面が無い。EventSystemもHome/Battleにしか無い
- 通信の失敗は`ApiException`が投げられるだけで、ほとんどの画面はログを出すだけ

方針:

1. **システムレイヤー**: Bootstrapシーン(常駐)に、シーンのUIより常に手前に出るCanvasを置き、Loadingと
   エラーダイアログを配置する。USNは使わずGameObjectの表示を切り替えるだけにする(Addressablesや通信の失敗時に
   出すものが、Addressablesの読み込みに依存しないように)。EventSystemもBootstrapへ移す
2. **公開の仕方**: 用途を絞ったサービスにする(`ISystemDialog`: 同時に1つだけ表示し、表示中の要求は順番待ち。
   `ILoadingIndicator`: 参照カウントで、0.3秒経っても終わらないときだけ表示。入力は即座に止める)
3. **エラーの扱い**: 画面では何もできないエラー(通信断・タイムアウト・5xx・再認証の失敗)だけを1か所で捕まえ、
   ダイアログでリトライさせる。4xxは意味が画面ごとに違うため、そのまま呼び出し元に投げる。同時に失敗した
   リクエストは1つのダイアログにまとめる。「タイトルへ」ではBootstrapシーンを読み直して起動からやり直す
   (Rootのスコープごと作り直すため、状態の消し漏れが起きない)
4. **二重送信の防止**: リトライでは「サーバーでは成功したが応答が届かなかった」リクエストも送り直す。
   送り直したときに何が起きるかをエンドポイントごとに調べた結果、対応が要るのは次のとおり

   | エンドポイント | 送り直した場合 | 対応 |
   |---|---|---|
   | `POST /scout/rolls` | ジェムが二重に引かれる | リクエストにクライアントが作る`requestId`(ULID)を足し、`scout_rolls`に`(player_id, request_id)`の一意制約を付ける。同じ`requestId`なら既存のロールを返す |
   | `POST /chat/send` | 同じ発言が2回載る | 同じく`requestId`で既存の発言を返す(チャット画面と合わせて行う) |
   | `POST /signup` | 409(作成済み) | クライアントで409を成功として扱う |
   | `POST /scout/rolls/{rollId}/select` | 409(選択済み) | クライアントで409を成功として扱い、所持データはサインインし直して合わせる |
   | `POST /devices` | 使われないデバイスが1件増える | 対応しない(害が無いため) |
   | その他(`/edit/*`・`/sign-in`・`/devices/authenticate`・GET) | 同じ結果になる | 不要 |

   汎用の`Idempotency-Key`ヘッダーとミドルウェア(全POSTの応答を保存する)は、対象が2つしか無く、応答の保存テーブルと
   期限切れの掃除を持つほどではないため採らない
5. 対戦(MagicOnion)の参加失敗・切断は対象外で、`BattlePresenter`がダイアログを直接使う

決めること: 「タイトルへ」の意味、リトライの上限、Loadingをすべての通信で自動にするか、捕まえ漏れの例外の通知、
タイムアウトの秒数。

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

`Shared/BattleContracts/Runtime/`の[IBattleHub.cs](../BattleContracts/Runtime/IBattleHub.cs)と
[BattlePayloads.cs](../BattleContracts/Runtime/BattlePayloads.cs)を正とする(MessagePackでシリアライズ)。
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

- 内部APIは`X-Internal-Secret`付きのREST(`/internal/battle/party`・`loadouts`・`result`、[3.9](#39-内部apibattleserver用))。
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
- 対戦相手ボット(`BattleBot`、`make bot`): REST(Unityと同じ順番でデバイス登録〜サインイン)とMagicOnionで自動対戦する。
  起動のたびに新しいプレイヤーを作り、`/debug/randomize_party`でパーティをランダムにする。選出はパーティから
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
| ステータスポイント(64ポイント)の割り振り・育成チケット・ショップの設計(APIとテーブル、能力の計算式への反映の仕方。`floor(努力値 / 4)`のままにするか) | [4.4](#44-所持データ)、[7.2](#72-atlasbattlecore) |
| 通信エラーダイアログ・Loading(gRPC化と合わせて決め直す) | [6.9](#69-通信エラーダイアログloading設計案) |
| APIサーバーのgRPC化([feature-grpc-foundation.md](proposals/feature-grpc-foundation.md)) | [3](#3-apiサーバー)、[6.6](#66-通信と所持データ) |
| Clientの再接続と、強制交代ターンの途中で再接続したときの状態の伝え方(`OnMatchStart`に強制交代の状態が無い) | [7.4](#74-対戦の進行) |
| 報酬額(初期300・勝利50)とスカウト・育成チケットの値段のバランス | [2.5](#25-所持リソースアイテム) |
| 対戦結果(勝敗・報酬)をHomeへ戻ったときにどう見せるか | [6.8](#68-バトル画面) |
| マスターデータのClientへの配布をCDN経由にするか | [5.3](#53-運用) |
| 能力の計算式がClient(`PachimonStatCalculator`)とBattleServer(`LoadoutBuilder`)の2か所にある | [7.2](#72-atlasbattlecore) |
