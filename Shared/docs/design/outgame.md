# アウトゲーム機能設計

デバイス認証、プレイヤー管理、チャット、所持パチモンの管理(パーティ編成・技の付け替え)
の詳細設計。全体構成・命名規則は[architecture.md](architecture.md)を参照。

## 概要

デバイス認証(ゲスト型アカウント)をベースに、プレイヤー管理・チャット・所持パチモン管理を
提供する。会員登録(メールアドレス/パスワード等によるアカウント作成)は学習用途では対象外とし、
`device_id`単位でのデバイス管理と、それに紐づく`player_id`単位でのゲームデータ管理を行う。

デバイス認証(トークン発行)・サインアップ(初回のみ、初期データセットアップ)・サインイン
(所持データ一式の取得)は目的の異なる別APIとして分離している。詳細は下記2・4・5番、および
[architecture.md](architecture.md)「APIレスポンス設計」参照。

以下「要認証」と記載したエンドポイントは`Authorization: Bearer <access_token>`ヘッダーを
必須とする。

## API仕様

| # | メソッド | パス | 説明 | 認証 |
|---|---|---|---|---|
| 1 | POST | `/devices` | デバイス新規登録 | 不要 |
| 2 | POST | `/devices/authenticate` | デバイス認証・アクセストークン取得 | 不要 |
| 3 | GET | `/auth/verify` | アクセストークン検証 | 要 |
| 4 | POST | `/signup` | サインアップ(プレイヤー作成・初期データセットアップ) | 要 |
| 5 | POST | `/sign-in` | サインイン(所持データ一式を`playerDiff`で取得) | 要 |
| 6 | POST | `/chat/send` | メッセージ送信 | 要 |
| 7 | GET | `/chat/poll` | メッセージ受信/取得 | 要 |
| 8 | POST | `/edit/party` | バトル用パーティ編成(6体まで) | 要 |
| 9 | POST | `/edit/pachimon_moves` | 技の付け替え | 要 |

### 1. デバイス新規登録

```
POST /devices
```

リクエスト

```json
{ "secretKey": "client-side-generated-random-string" }
```

レスポンス

```json
{ "deviceId": "my-device-id" }
```

> `player_id`はこのエンドポイントでは発行しない。デバイス登録は「端末を識別する」処理と
> 「ゲームデータの主体(プレイヤー)を作る」処理を分離しており、プレイヤー作成は`/signup`で
> 別途行う(下記4番参照)。

### 2. デバイス認証

```
POST /devices/authenticate
```

リクエスト

```json
{ "deviceId": "my-device-id", "secretKey": "my-secret-key" }
```

レスポンス

```json
{ "accessToken": "my-access-token", "expiresIn": 360 }
```

設計方針: 1つの`device_id`につき有効なアクセストークンは常に1つのみ。再認証時は古い
トークンを新しいトークンで上書き(無効化)する。

> 補足(クライアント実装時の考慮事項): 本設計では別途「リフレッシュトークン」を用意せず、
> `device_id` + `secret_key`による`/devices/authenticate`の再実行がリフレッシュを兼ねる。
> クライアント実装時は、事前に有効期限をチェックして早めに再認証する処理と、401が返って
> きた場合に再認証してリトライする処理を併用するのが望ましい。

### 3. アクセストークン検証

```
GET /auth/verify
```

有効であれば`200`、無効(期限切れ・存在しない)であれば`401`を返す。

### 4. サインアップ(プレイヤー作成)

```
POST /signup
```

デバイス認証後、初回起動時などに呼び出し、ゲームデータの主体となる`player`を作成して
`device_id`に紐付ける。1つの`device_id`につき`player`は1件のみ(`UNIQUE`)。

リクエスト

```json
{ "nickname": "プレイヤー名" }
```

レスポンス: `200`(ボディ無し)

`player`作成と同一トランザクションで、`starter_party_slots`マスタ(下記「スターター編成」
参照)の内容をそのまま複製して初期パーティ(`player_pachimon`・`player_party_slots`)を
自動付与する。プレイヤーは作成直後から対戦可能な状態になる(選択制ではなく、全プレイヤー
共通の単一固定編成)。同じトランザクションで`player_items`(下記「DB設計」参照)に
`item_id: 1`(gems)・`quantity: 300`の行も作成する。gemsの初期値(`300`)と、対戦報酬による
gems付与の設計は[battle.md](battle.md)の「報酬設計(gems)」を参照。

作成したプレイヤーのデータはこのレスポンスでは返さない。クライアントは`POST /signup`
成功後に必ず`POST /sign-in`を呼び、所持データ一式をまとめて取得する(下記5番参照)。

### 5. サインイン

```
POST /sign-in
```

`POST /devices/authenticate`(デバイス認証)の直後に毎回呼び出す。プレイヤーが必要とする
所持データ一式を`playerId`/`nickname`と`playerDiff`([architecture.md](architecture.md)参照)
にまとめて返す。旧`GET /players/me`・`GET /players/me/pachimon`はこれに統合されたため廃止した。

リクエストボディ無し(`Authorization: Bearer <access_token>`のみ)

レスポンス

```json
{
  "playerId": "my-player-id",
  "nickname": "プレイヤー名",
  "playerDiff": {
    "items": {
      "upserted": [ { "itemId": 1, "quantity": 300 } ],
      "removed": []
    },
    "pachimon": {
      "upserted": [ { "playerPachimonId": "...", "pachimonId": 12 } ],
      "removed": []
    },
    "pachimonMoveMap": {
      "upserted": [
        { "playerPachimonMoveId": "...", "playerPachimonId": "...", "slot": 1, "moveId": 3 }
      ],
      "removed": []
    },
    "partySlots": {
      "upserted": [ { "partySlotId": "...", "slot": 1, "playerPachimonId": "..." } ],
      "removed": []
    }
  }
}
```

サインインは常に所持データの全件を返す(差分ではなくフルスナップショットとして機能する)
ため、`playerDiff`各リソースの`removed`は常に空配列になる。

プレイヤー未作成の場合は`404`を返す(旧`GET /players/me`の挙動を踏襲)。クライアントは
この`404`を検知した場合のみ`POST /signup`(サインアップ)を呼んでから、改めて
`POST /sign-in`を呼び直す。

### 6. チャット送信

```
POST /chat/send
```

チャットルームの概念はなし。全プレイヤー共通の1つのチャットに送信する。

リクエスト

```json
{ "content": "メッセージ本文" }
```

レスポンス

```json
{ "messageId": "my-message-id", "createdAt": "2026-09-19T12:00:00.000Z" }
```

### 7. チャット受信/取得

```
GET /chat/poll
```

全プレイヤー共通のメッセージ一覧を取得する。

レスポンス

```json
{
  "messages": [
    { "messageId": "...", "playerId": "...", "content": "...", "createdAt": "2026-09-19T12:00:00.000Z" }
  ]
}
```

### 8. パーティ編成

```
POST /edit/party
```

リクエスト

```json
{ "partySlots": [ { "slot": 1, "playerPachimonId": "..." } ] }
```

リクエストに含まれないslotの既存編成は解除される(全置き換え)。

レスポンス: `playerDiff`([architecture.md](architecture.md)参照)のみ(プロフィールは変化しない
ため`profile`は含めない)。`partySlots.upserted`に今回設定した分、`partySlots.removed`に
今回の呼び出しで解除された(リクエストから外れた)`partySlotId`を載せる。`pachimon`/
`pachimonMoveMap`は変化しないため`upserted`/`removed`とも空配列。

```json
{
  "playerDiff": {
    "items": { "upserted": [], "removed": [] },
    "pachimon": { "upserted": [], "removed": [] },
    "pachimonMoveMap": { "upserted": [], "removed": [] },
    "partySlots": {
      "upserted": [ { "partySlotId": "...", "slot": 1, "playerPachimonId": "..." } ],
      "removed": [ "..." ]
    }
  }
}
```

**バリデーション**

1. 人数は1〜6体(0体では`/battle/queue`への参加不可)
2. 同一`player_pachimon_id`を複数slotに設定不可
3. 同一パチモンの重複は許可(本家の「同種族1体まで」制約は入れない)
4. 指定した`player_pachimon_id`が呼び出したプレイヤー自身の所持個体であることをサーバー側で検証

### 9. 技の付け替え

```
POST /edit/pachimon_moves
```

グループ内の候補技(`move_group_moves`)から選択する。マスタ側の変更は不要で、
`player_pachimon_moves`の対象slotをUPDATEするだけで実現できる。対象の`playerPachimonId`・
`slot`はURLパスではなくリクエストボディに含める(POST化に合わせて、識別子をパスパラメータに
分散させず1つのボディにまとめる方針)。

リクエスト

```json
{ "playerPachimonId": "...", "slot": 1, "moveId": 7 }
```

レスポンス: `playerDiff`のみ(プロフィールは変化しないため`profile`は含めない)。付け替え後の
割当1件を`pachimonMoveMap.upserted`に載せる。`pachimon`本体は変化していないため送り直さない。
`partySlots`も変化しないため空配列。

```json
{
  "playerDiff": {
    "items": { "upserted": [], "removed": [] },
    "pachimon": { "upserted": [], "removed": [] },
    "pachimonMoveMap": {
      "upserted": [
        { "playerPachimonMoveId": "...", "playerPachimonId": "...", "slot": 1, "moveId": 7 }
      ],
      "removed": []
    },
    "partySlots": { "upserted": [], "removed": [] }
  }
}
```

## DB設計

`player_id`をはじめ、プレイヤーが都度生成するデータのIDにはULID
(Universally Unique Lexicographically Sortable Identifier)を採用する。生成時刻順に
ソート可能・26文字固定長という特徴を持つ。Rustでは`ulid`クレートで生成し、DBには
`CHAR(26)`で文字列のまま保持する。

### devices

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `device_id` | CHAR(26) | PRIMARY KEY | ULID |
| `secret_key_hash` | VARCHAR(255) | NOT NULL | `secret_key`をハッシュ化して保存 |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | 登録日時 |

### access_tokens

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `device_id` | CHAR(26) | PRIMARY KEY, FOREIGN KEY → `devices.device_id` | 1device_id = 1トークン。再認証時はUPSERTで上書き |
| `access_token` | VARCHAR(64) | NOT NULL, UNIQUE | ランダム生成した文字列 |
| `expires_at` | DATETIME(3) | NOT NULL | 有効期限(発行時刻 + `expires_in`秒) |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | 発行日時 |

### players

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_id` | CHAR(26) | PRIMARY KEY | ULID |
| `device_id` | CHAR(26) | NOT NULL, UNIQUE, FOREIGN KEY → `devices.device_id` | 紐づくデバイス |
| `nickname` | VARCHAR(50) | NOT NULL | プレイヤー名 |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | 作成日時 |

gems等の所持数は本テーブルに持たず、`player_items`(下記)で管理する。

### player_items(所持アイテム)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_id` | CHAR(26) | PRIMARY KEY(複合), FOREIGN KEY → `players.player_id` | |
| `item_id` | INT | PRIMARY KEY(複合), FOREIGN KEY → `items.item_id` | `items`マスタ(architecture.md「マスターデータ設計」参照)。`item_id: 1`がgems |
| `quantity` | INT | NOT NULL | 所持数(絶対値) |
| `updated_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3) | |

`player_pachimon`のような割当エンティティ(ULID主キー)ではなく、`(player_id, item_id)`複合PKの
単純な所持数テーブルにした。アイテムは「何個持っているか」だけが意味を持ち、パーティ編成や
技の付け替えのように個々の割当を独立したエンティティとして参照する必要が無いため。
`POST /signup`で`item_id: 1`(gems)・`quantity: 300`の行を作成し、以後は
`INSERT ... ON DUPLICATE KEY UPDATE`で`quantity`を増減する(スカウトのgems消費は
[scout.md](scout.md)、対戦報酬のgems付与は[battle.md](battle.md)参照)。

### messages

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `message_id` | CHAR(26) | PRIMARY KEY | ULID |
| `player_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | 送信者 |
| `content` | TEXT | NOT NULL | メッセージ本文 |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | 送信日時 |

`created_at`にインデックスを張り、`poll`時の新しい順取得を高速化する。

### player_pachimon

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_pachimon_id` | CHAR(26) | PRIMARY KEY | ULID |
| `player_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | |
| `pachimon_id` | INT | NOT NULL, FOREIGN KEY → `pachimon.pachimon_id` | |
| `effort_values` | JSON | NOT NULL | 例: `{"hp":0,"atk":0,"def":0,"spatk":0,"spdef":0,"speed":0}`、デフォルト全0(将来の努力値64ポイント配分用、現状は未使用) |
| `obtained_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | |

パーティ編成状況は本テーブルの属性としては持たず、`player_party_slots`(下記)に分離する。

個体値(IV)の概念は持たない(ポケモンチャンピオンズ準拠で廃止)。個体差は努力値
(`effort_values`)のみで表現する(実効ステータス計算式は[battle.md](battle.md)参照)。

### player_party_slots(パーティ編成)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `party_slot_id` | CHAR(26) | PRIMARY KEY | ULID。割当自体を独立したエンティティとして扱う |
| `player_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | |
| `slot` | INT | NOT NULL | 1-6。`UNIQUE(player_id, slot)` |
| `player_pachimon_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `player_pachimon.player_pachimon_id` | `UNIQUE(player_id, player_pachimon_id)`(同一個体を複数slotに置けない) |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | |

`player_pachimon.party_slot`のようなnullableな属性ではなく専用テーブルにした理由:
未編成を「行が存在しない」ことで表現でき、API応答で`nullable`を使わずに済む
(`api-codegen`が現状`nullable`未対応のため。`Shared/docs/progress.md`参照)。パーティ編成
(`POST /edit/party`)は既存行を全削除してから指定されたslot分だけ新しいULIDで
再作成する(全置き換え)。

### starter_party_slots(スターター編成マスタ)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `slot_no` | BIGINT | PRIMARY KEY | 1-6 |
| `pachimon_id` | BIGINT | NOT NULL, FOREIGN KEY → `pachimon.pachimon_id` | |

`master-data-pipeline`が生成するマスタテーブル(`Shared/master-data/schema/tables/`
参照)。全プレイヤー共通の単一固定編成(選択制ではない)で、`pachimon_id`は必ず埋まっている
(nullable無し)。`POST /signup`(プレイヤー作成)時にこの内容をそのまま複製して
`player_pachimon`(初期技込み)・`player_party_slots`を生成する
(`src/service/player_service.rs::grant_starter_party`参照)。他のマスタ同様、更新の反映には
`seed_master_data`実行とサーバー再起動が必要(無停止反映は非対応)。

### player_pachimon_moves(現在覚えている技)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_pachimon_move_id` | CHAR(26) | PRIMARY KEY | ULID。割当自体を独立したエンティティとして扱う(他テーブルと同様の方針) |
| `player_pachimon_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `player_pachimon.player_pachimon_id` | |
| `slot` | INT | NOT NULL | 1-4。`UNIQUE(player_pachimon_id, slot)` |
| `move_id` | INT | NOT NULL, FOREIGN KEY → `moves.move_id` | |

スカウトで個体が生成される際、対応する`move_group_moves`の`is_initial = TRUE`の行をそのまま
複製して初期セットする(詳細は[scout.md](scout.md)参照)。技の付け替えは、このテーブルの
対象slotをUPDATEするだけで実現でき、マスタ側の変更は不要。`INSERT ... ON DUPLICATE KEY UPDATE`
の`UPDATE`句に`player_pachimon_move_id`を含めないことで、既存slotの付け替え時はこのIDが
変わらず維持される(新規slotへの初回セット時のみ新しいULIDが採番される)。

**レベルについて**: このゲームはバトルが主目的であり、経験値によるレベルアップという育成要素は
持たない。全パチモンは内部的に固定レベル50(競技対戦フォーマットの慣例)として扱う。
プレイヤーごと・個体ごとに変動する値ではないため`player_pachimon`にカラムを持たせず、
`Atlas.BattleCore`側の定数として保持する(詳細は[battle.md](battle.md)参照)。

## 未確定の論点

- 努力値(64ポイント)の自由配分機能とその上限チェックは未実装(`effort_values`カラムは用意済み)
