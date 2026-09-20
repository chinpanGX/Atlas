# アウトゲーム機能設計

デバイス認証、プレイヤー管理、チャット、所持パチモンの管理(パーティ編成・技の付け替え)
の詳細設計。全体構成・命名規則は[architecture.md](architecture.md)を参照。

## 概要

デバイス認証(ゲスト型アカウント)をベースに、プレイヤー管理・チャット・所持パチモン管理を
提供する。サインアップ/サインイン(会員登録)は学習用途では対象外とし、`device_id`単位での
デバイス管理と、それに紐づく`player_id`単位でのゲームデータ管理を行う。

以下「要認証」と記載したエンドポイントは`Authorization: Bearer <access_token>`ヘッダーを
必須とする。

## API仕様

| # | メソッド | パス | 説明 | 認証 |
|---|---|---|---|---|
| 1 | POST | `/devices` | デバイス新規登録 | 不要 |
| 2 | POST | `/devices/authenticate` | デバイス認証・アクセストークン取得 | 不要 |
| 3 | GET | `/auth/verify` | アクセストークン検証 | 要 |
| 4 | POST | `/players` | プレイヤー作成(device認証後に紐付け) | 要 |
| 5 | GET | `/players/me` | 自分のプレイヤー情報取得 | 要 |
| 6 | POST | `/chat/send` | メッセージ送信 | 要 |
| 7 | GET | `/chat/poll` | メッセージ受信/取得 | 要 |
| 8 | GET | `/players/me/pachimon` | 所持パチモン一覧 | 要 |
| 9 | PUT | `/players/me/party` | バトル用パーティ編成(6体まで) | 要 |
| 10 | PUT | `/players/me/pachimon/{player_pachimon_id}/moves/{slot}` | 技の付け替え | 要 |

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
> 「ゲームデータの主体(プレイヤー)を作る」処理を分離しており、プレイヤー作成は`/players`で
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

### 4. プレイヤー作成

```
POST /players
```

デバイス認証後、初回起動時などに呼び出し、ゲームデータの主体となる`player`を作成して
`device_id`に紐付ける。1つの`device_id`につき`player`は1件のみ(`UNIQUE`)。

リクエスト

```json
{ "nickname": "プレイヤー名" }
```

レスポンス

```json
{ "playerId": "my-player-id", "nickname": "プレイヤー名", "gems": 300 }
```

`gems`の初期値(`300`)と、対戦報酬によるgems付与の設計は[battle.md](battle.md)の
「報酬設計(gems)」を参照。

### 5. 自分のプレイヤー情報取得

```
GET /players/me
```

レスポンス

```json
{ "playerId": "my-player-id", "nickname": "プレイヤー名", "gems": 100 }
```

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

### 8. 所持パチモン一覧

```
GET /players/me/pachimon
```

レスポンス

```json
{
  "pachimon": [
    {
      "playerPachimonId": "...",
      "pachimonId": 12,
      "level": 5,
      "partySlot": null,
      "moves": [ { "slot": 1, "moveId": 3 } ]
    }
  ]
}
```

### 9. パーティ編成

```
PUT /players/me/party
```

リクエスト

```json
{ "partySlots": [ { "slot": 1, "playerPachimonId": "..." } ] }
```

**バリデーション**

1. 人数は1〜6体(0体では`/battle/queue`への参加不可)
2. 同一`player_pachimon_id`を複数slotに設定不可
3. 同一パチモンの重複は許可(本家の「同種族1体まで」制約は入れない)
4. 指定した`player_pachimon_id`が呼び出したプレイヤー自身の所持個体であることをサーバー側で検証
5. レベル制限はなし(将来拡張)

### 10. 技の付け替え

```
PUT /players/me/pachimon/{player_pachimon_id}/moves/{slot}
```

グループ内の候補技(`move_group_master`)から選択する。マスタ側の変更は不要で、
`player_pachimon_moves`の対象slotをUPDATEするだけで実現できる。

リクエスト

```json
{ "moveId": 7 }
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
| `gems` | INT | NOT NULL, DEFAULT 300 | スカウト用課金石。初期付与・対戦報酬の設計は[battle.md](battle.md)参照 |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | 作成日時 |

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
| `level` | INT | NOT NULL | |
| `ivs` | JSON | NOT NULL | |
| `party_slot` | INT | NULL可 | 1-6、NULL=ボックス。`UNIQUE(player_id, party_slot)` |
| `effort_values` | JSON | NOT NULL | 例: `{"hp":0,"atk":0,"def":0,"spatk":0,"spdef":0,"speed":0}`、デフォルト全0(将来の努力値64ポイント配分用、現状は未使用) |
| `obtained_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | |

### player_pachimon_moves(現在覚えている技)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_pachimon_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `player_pachimon.player_pachimon_id` | |
| `slot` | INT | NOT NULL | 1-4 |
| `move_id` | INT | NOT NULL, FOREIGN KEY → `moves.move_id` | |

`UNIQUE(player_pachimon_id, slot)`

スカウトで個体が生成される際、対応する`move_group_master`の`is_initial = TRUE`の行をそのまま
複製して初期セットする(詳細は[scout.md](scout.md)参照)。技の付け替えは、このテーブルの
対象slotをUPDATEするだけで実現でき、マスタ側の変更は不要。

## 未確定の論点

- パーティ編成のレベル制限は将来拡張として保留
- 努力値(64ポイント)の自由配分機能とその上限チェックは未実装(`effort_values`カラムは用意済み)
