# Atlas Server 設計書

## 概要

デバイス認証(ゲスト型アカウント)をベースに、プレイヤー管理・チャット・パチモンスカウト・対戦マッチングを提供するREST APIサーバー(Rust/Axum)。サインアップ/サインイン(会員登録)は学習用途では対象外とし、`device_id`単位でのデバイス管理と、それに紐づく`player_id`単位でのゲームデータ管理を行う。

対戦(バトル)中のリアルタイム判定はC#/MagicOnionサーバーが別途担当し、本サーバー(Rust)はDBの唯一の書き込み口として、認証・マッチング・対戦結果の記録を行う。MagicOnion側の設計(Hubインターフェース・ダメージ計算式等)は`design.md`を参照。

## 命名規則

- DB/Rust: `snake_case`で統一。マスタデータは修飾語なし、プレイヤー所持データは必ず`player_`プレフィックスを付ける(`player_pachimon`, `player_pachimon_moves`等)
- REST APIのJSONペイロードは`serde(rename_all = "camelCase")`を用い、DB/Rust内部は`snake_case`のままJSONのみ`camelCase`に変換する(本ドキュメントのリクエスト/レスポンス例も`camelCase`で記載)
- 「ポケモン」を指す語はプロジェクト名にちなみ`pachimon`で統一(商標混同を避けるための独自名)
- 以下「要認証」と記載したエンドポイントは`Authorization: Bearer <access_token>`ヘッダーを必須とする

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
| 8 | GET | `/scout/banners` | 開催中のスカウトバナー一覧 | 要 |
| 9 | POST | `/scout/banners/{id}/pull` | スカウト実行(1回/10連) | 要 |
| 10 | GET | `/players/me/pachimon` | 所持パチモン一覧 | 要 |
| 11 | PUT | `/players/me/party` | バトル用パーティ編成(6体まで) | 要 |
| 12 | PUT | `/players/me/pachimon/{player_pachimon_id}/moves/{slot}` | 技の付け替え | 要 |
| 13 | POST | `/battle/queue` | 待機列に参加 | 要 |
| 14 | DELETE | `/battle/queue` | 待機列から離脱 | 要 |
| 15 | GET | `/battle/queue/status` | マッチ成立確認(polling) | 要 |
| 16 | POST | `/internal/battle/result` | 対戦結果・ログをまとめて記録(内部API) | サービス間シークレット |

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

> `player_id`はこのエンドポイントでは発行しない。デバイス登録は「端末を識別する」処理と「ゲームデータの主体(プレイヤー)を作る」処理を分離しており、プレイヤー作成は`/players`で別途行う(下記4番参照)。

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

設計方針:1つの`device_id`につき有効なアクセストークンは常に1つのみ。再認証時は古いトークンを新しいトークンで上書き(無効化)する。多くのモバイルゲームでも、同一アカウントの多重ログインは元々想定されない/意図的に弾く実装が一般的なため、この方針で問題ない。

> 補足(クライアント実装時の考慮事項):本設計では別途「リフレッシュトークン」を用意せず、`device_id` + `secret_key`による`/devices/authenticate`の再実行がリフレッシュを兼ねる。クライアント実装時は、事前に有効期限をチェックして早めに再認証する処理と、401が返ってきた場合に再認証してリトライする処理を併用するのが望ましい(実装は将来のクライアント側実装時の課題とする)。

### 3. アクセストークン検証

```
GET /auth/verify
```

有効であれば`200`、無効(期限切れ・存在しない)であれば`401`を返す。

### 4. プレイヤー作成

```
POST /players
```

デバイス認証後、初回起動時などに呼び出し、ゲームデータの主体となる`player`を作成して`device_id`に紐付ける。1つの`device_id`につき`player`は1件のみ(`UNIQUE`)。

リクエスト
```json
{ "nickname": "プレイヤー名" }
```

レスポンス
```json
{ "playerId": "my-player-id", "nickname": "プレイヤー名", "gems": 0 }
```

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

### 8. スカウトバナー一覧

```
GET /scout/banners
```

開催中のスカウトバナー(排出率グループ)一覧を取得する。

レスポンス
```json
{
  "banners": [
    { "bannerId": "...", "name": "ピックアップスカウト", "costPerPull": 150, "startAt": "...", "endAt": "..." }
  ]
}
```

### 9. スカウト実行

```
POST /scout/banners/{id}/pull
```

リクエスト
```json
{ "pullCount": 1 }
```

`pullCount`は`1`または`10`。10連は「1連を10回呼ぶ」実装で十分(レア確定枠・ピックアップ演出は初期スコープ外)。

レスポンス
```json
{
  "results": [
    { "playerPachimonId": "...", "pachimonId": 12, "rarity": "S" }
  ],
  "gems": 850
}
```

**抽選ロジック(Rust側)**
1. `rate_table`から乱数でレアリティを1つ抽選(重み付き抽選、同レアリティ内は均等抽選)
2. 決まったレアリティに紐づく`pachimon`(`rarity`カラム一致)を全件取得し、等確率で1件をランダム選出
3. `player_pachimon`に登録し、`move_group_moves`の`is_initial = TRUE`の技を`player_pachimon_moves`にコピー
4. `scout_pulls`に結果を記録

### 10. 所持パチモン一覧

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

### 11. パーティ編成

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

### 12. 技の付け替え

```
PUT /players/me/pachimon/{player_pachimon_id}/moves/{slot}
```

グループ内の候補技(`move_group_moves`)から選択する。マスタ側の変更は不要で、`player_pachimon_moves`の対象slotをUPDATEするだけで実現できる。

リクエスト
```json
{ "moveId": 7 }
```

### 13. マッチング待機列に参加

```
POST /battle/queue
```

パーティが1体以上編成されていることが前提。

### 14. マッチング待機列から離脱

```
DELETE /battle/queue
```

### 15. マッチ成立確認

```
GET /battle/queue/status
```

polling方式(1秒程度の遅延はゲーム体験に影響しないため、WSを別途入れるコストに対してメリットが薄いと判断)。

レスポンス(マッチ成立時)
```json
{
  "status": "matched",
  "matchId": "01J...",
  "battleServer": "https://battle.example.com",
  "battleToken": "短命JWT(match_id, player_idを含む)"
}
```

> マッチング待機列自体はDB永続化せず、Rustプロセスのメモリ(チャネル等)で管理する(サーバー1台構成のため問題なし)。

### 16. 対戦結果記録(内部API)

```
POST /internal/battle/result
```

MagicOnionサーバーから対戦終了時に呼び出される。内部ネットワークのみ疎通、サービス間シークレットで保護する(エンドユーザーの`access_token`とは別軸の認証)。

リクエスト
```json
{
  "matchId": "...",
  "winnerId": "...",
  "player1SelectedPachimon": ["...", "...", "..."],
  "player2SelectedPachimon": ["...", "...", "..."],
  "turns": [
    { "turnNumber": 1, "playerId": "...", "actionData": { }, "resultData": { } }
  ]
}
```

`battle_matches`のステータス更新・`winner_id`設定と、`battle_turns`への一括INSERTを行う。

## DB設計

`player_id`をはじめ、プレイヤーが都度生成するデータのIDにはULID(Universally Unique Lexicographically Sortable Identifier)を採用する。UUIDと同様に衝突しにくいID体系でありながら、生成時刻順にソート可能・26文字固定長という特徴を持つ。Rustでは`ulid`クレートで生成でき、DBには`CHAR(26)`で文字列のまま保持する(速度重視なら`BINARY(16)`エンコードも選択肢)。一方、`pachimon`や`moves`等のマスタデータは運用者が投入する固定データのため、`INT AUTO_INCREMENT`を採用する。

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

`device_id`をPRIMARY KEYとすることで、1デバイスにつき常に1レコード(1トークン)のみ存在する状態を保証する。再認証時はUPSERT(既存レコードがあれば更新)で対応する。

### players

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `player_id` | CHAR(26) | PRIMARY KEY | ULID |
| `device_id` | CHAR(26) | NOT NULL, UNIQUE, FOREIGN KEY → `devices.device_id` | 紐づくデバイス |
| `nickname` | VARCHAR(50) | NOT NULL | プレイヤー名 |
| `gems` | INT | NOT NULL, DEFAULT 0 | スカウト用課金石 |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | 作成日時 |

### messages

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `message_id` | CHAR(26) | PRIMARY KEY | ULID |
| `player_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | 送信者 |
| `content` | TEXT | NOT NULL | メッセージ本文 |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | 送信日時 |

`created_at`にインデックスを張り、`poll`時の新しい順取得を高速化する。

### move_groups(技グループマスタ)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `move_group_id` | INT | PRIMARY KEY, AUTO_INCREMENT | |
| `name` | VARCHAR(100) | NOT NULL | 管理用ラベル |

複数のパチモンが同じグループを共有できるため、新規パチモン追加は基本的に`pachimon`への1行追加(既存グループを指すだけ)で済む。

### moves(技マスタ)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `move_id` | INT | PRIMARY KEY, AUTO_INCREMENT | |
| `name` | VARCHAR(100) | NOT NULL | |
| `type` | VARCHAR(20) | NOT NULL | |
| `category` | ENUM('physical','special','status') | NOT NULL | |
| `base_power` | INT | NULL可 | 状態技の場合はNULL |
| `accuracy` | INT | NOT NULL | |
| `max_pp` | INT | NOT NULL | |

追加効果(状態異常付与・能力変化など)は排除。状態技自体は定義できるが効果の実装は対象外。

### move_group_moves(グループが含む技の対応表)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `move_group_id` | INT | NOT NULL, FOREIGN KEY → `move_groups.move_group_id` | |
| `move_id` | INT | NOT NULL, FOREIGN KEY → `moves.move_id` | |
| `is_initial` | BOOLEAN | NOT NULL, DEFAULT FALSE | 初期習得技かどうか(グループ内で最大4件になるよう運用で担保) |

`UNIQUE(move_group_id, move_id)`

### pachimon(マスタ、実装済み)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `pachimon_id` | BIGINT | PRIMARY KEY | master-data-pipeline(スプレッドシート)側で採番。AUTO_INCREMENTにしない |
| `name` | VARCHAR(100) | NOT NULL | |
| `primary_type` | TINYINT UNSIGNED | NOT NULL | `PachimonType` Enumの数値表現(0-18) |
| `secondary_type` | TINYINT UNSIGNED | NOT NULL | `PachimonType` Enumの数値表現。無い場合は`0`(`PachimonType::None`) |
| `base_hp` / `base_atk` / `base_def` / `base_spatk` / `base_spdef` / `base_speed` | BIGINT | NOT NULL | 種族値 |
| `rarity` | TINYINT UNSIGNED | NOT NULL | `Rarity` Enumの数値表現(1-4)。スカウトの排出率グルーピング用 |
| `move_group_id` | BIGINT | NOT NULL | `move_groups`未実装のため、現時点では外部キー制約なし |

> 実装メモ: 当初はDB設計上`primary_type`等をタイプ名の`VARCHAR`で想定していたが、実際の`master-data-pipeline`は`PachimonType`/`Rarity`を数値Enumとして生成するため、生成コードと1:1になるよう`BIGINT`に変更し、さらに実際の値域(`PachimonType`: 0-18、`Rarity`: 1-4)に対してBIGINTは過大だったため`TINYINT UNSIGNED`(0-255)へ変更した(スキーマの正しさ・自己文書化が目的。起動時に1回DBから読み込むだけの設計のためパフォーマンス上の意味合いは小さい)。`primary_type`/`secondary_type`/`rarity`の数値→Enumデコードは、生成された`Deserialize`実装をそのまま再利用している(`src/master/cache.rs`)。

### type_chart(タイプ相性マスタ)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `attack_type` | VARCHAR(20) | PRIMARY KEY(複合) | |
| `defend_type` | VARCHAR(20) | PRIMARY KEY(複合) | |
| `multiplier` | DECIMAL(3,2) | NOT NULL | 0 / 0.5 / 1 / 2 |

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

スカウトで個体が生成される際、対応する`move_group_moves`の`is_initial = TRUE`の行をそのまま複製して初期セットする。

### scout_banners(マスタ)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `banner_id` | CHAR(26) | PRIMARY KEY | ULID |
| `name` | VARCHAR(100) | NOT NULL | |
| `rate_table` | JSON | NOT NULL | レアリティ別確率。合計1.0(例: `{"S":0.03,"A":0.12,"B":0.35,"C":0.50}`) |
| `cost_per_pull` | INT | NOT NULL | |
| `start_at` | DATETIME(3) | NOT NULL | |
| `end_at` | DATETIME(3) | NOT NULL | |

### scout_pulls(履歴)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `pull_id` | CHAR(26) | PRIMARY KEY | ULID |
| `player_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | |
| `banner_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `scout_banners.banner_id` | |
| `pachimon_id` | INT | NOT NULL, FOREIGN KEY → `pachimon.pachimon_id` | 結果 |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | |

### battle_matches

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `match_id` | CHAR(26) | PRIMARY KEY | ULID |
| `player1_id` / `player2_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | |
| `status` | ENUM('matching','in_progress','finished') | NOT NULL | |
| `winner_id` | CHAR(26) | NULL可, FOREIGN KEY → `players.player_id` | |
| `player1_selected_pachimon` / `player2_selected_pachimon` | JSON | NULL可 | 選出された`player_pachimon_id`3体分(自動選出でも実データとして記録) |
| `started_at` / `ended_at` | DATETIME(3) | NULL可 | |

### battle_turns(対戦ログ)

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `turn_id` | CHAR(26) | PRIMARY KEY | ULID |
| `match_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `battle_matches.match_id` | |
| `turn_number` | INT | NOT NULL | |
| `player_id` | CHAR(26) | NOT NULL, FOREIGN KEY → `players.player_id` | 行動者 |
| `action_data` | JSON | NOT NULL | |
| `result_data` | JSON | NOT NULL | |
| `created_at` | DATETIME(3) | NOT NULL, DEFAULT CURRENT_TIMESTAMP(3) | |

補足:
- `battle_server_id`はサーバー1台構成のため不要と判断し省略
- マッチング待機列(`matchmaking_queue`)はDB永続化せず、Rustプロセスのメモリ(チャネル等)で管理(1台構成のため問題なし)
- 対戦中の判定ロジック(ダメージ計算式・命中判定・行動順序等)およびMagicOnion Hubインターフェースの詳細は`design.md`を参照

## マスターデータ管理

マスタデータ(`pachimon`, `moves`, `move_groups`, `move_group_moves`, `type_chart`, `scout_banners`)の管理・配信方針。ソーシャルゲーム開発の現場で一般的な「スプレッドシート→CI→DB反映」パターン([GREE Engineering「モバイルゲームにおけるマスターデータ運用事例」](https://labs.gree.jp/blog/2015/12/15368/)等を参考)を踏襲しつつ、本プロジェクトの規模(学習用途・サーバー1台構成)に合わせて簡略化している。

### 正の保存先とパイプライン

- 正(source of truth)はMySQL。スプレッドシート → CI(`master-data-pipeline`)でJSON/CSVとRust型定義を生成しGitでバージョン管理 → MySQLへseed投入、という既存パイプラインを継続利用する
- スキーマ変更(DDL)は`sqlx migrate`で管理し、マスタデータの中身(DML)は別のseedコマンドで管理する。migrationファイルにデータをINSERTしない(データ更新のたびにmigrationファイルが増え続け、履歴が肥大化するのを避けるため)

### サーバー側の読み取り方式

- Rust/Axumサーバーは、リクエストのたびにマスタテーブルへ問い合わせない。**起動時にDBから全マスタを1回読み込み、`Arc<MasterData>`としてメモリに保持する**。各リクエストハンドラはこのキャッシュを参照するだけで、リクエスト処理中にDBアクセスは発生させない
- マスタ更新の反映は「CIがMySQLへ再seed → サーバー再起動」で行う。本プロジェクトは1台構成のローカル運用が前提(`design.md`参照)であり、無停止反映のための常時ポーリング(バックグラウンドタスクによる更新検知等)は、現時点の規模では過剰と判断し採用しない
- 無停止反映が将来必要になった場合は、`POST /internal/master/reload`のような明示的なリロードエンドポイントをCIから叩く方式へ拡張できる

### 将来の複数台構成への拡張(現時点では未採用)

- 複数台のAPIサーバーが稼働するようになった場合は、`master_data_version`のようなハッシュ値テーブル(またはRedis等の共有ストア)を各サーバーがポーリングし、変更を検知したサーバーだけがキャッシュを再読み込みする方式に格上げする
- 現時点(1台構成)ではこの仕組みは導入しない(不要な複雑化を避けるため)

### 未解決事項

- MagicOnion(C#)側もバトル判定にマスタデータ(種族値・技威力・タイプ相性等)を必要とするが、DBを直接見ない設計のため、上記の仕組みをそのまま適用できない。マスタの入手経路(①MagicOnionも読み取り専用でMySQLに接続する、②Rust側が内部APIとしてマスタを配信する、等)は別途検討する

### 実装状況

`pachimon`について上記方針を実装済み。

- `src/master/mod.rs`: `master_data/pachimon.json`(master-data-pipelineの生成物)をビルド時に埋め込み、`seed_pachimon_data()`でシード用データとして参照する
- `src/bin/seed_master_data.rs`: `cargo run --bin seed_master_data`で`pachimon`テーブルへUPSERT(冪等)投入するコマンド
- `src/master/cache.rs`: `MasterData::load(pool)`でDBの`pachimon`テーブルから全件読み込み、`PachimonType`/`Rarity`の数値表現を生成済み`Deserialize`実装で復元する
- `src/state.rs`: `AppState::new`/`AppState::from_pool`が起動時に`MasterData::load`を実行し、`Arc<MasterData>`として`AppState.master`に保持する。各リクエストハンドラはこれを参照するだけでDBアクセスは発生しない(現時点ではまだ参照するハンドラ自体は未実装)
- `moves` / `move_groups` / `move_group_moves` / `type_chart` / `scout_banners`は、対応するmaster-data-pipelineの生成物が無いため未実装。スカウトAPI実装時に同じパターンで追加する

## マイグレーション手順

上記のテーブル定義を、`sqlx-cli`でマイグレーションファイルとして反映する(`sqlx-cli`自体の導入は`init.md`を参照)。FK依存関係の順に、マイグレーションファイルの雛形を作成する。

> `devices` / `access_tokens` / `players` / `messages` / `pachimon`は実装済み(`migrations/`配下に反映済み)。それ以外(`move_groups`以降)は未実装で、以下はそのための計画。

```bash
sqlx migrate add create_devices_table       # 実装済み
sqlx migrate add create_access_tokens_table # 実装済み
sqlx migrate add create_players_table       # 実装済み
sqlx migrate add create_messages_table      # 実装済み
sqlx migrate add create_move_groups_table
sqlx migrate add create_moves_table
sqlx migrate add create_move_group_moves_table
sqlx migrate add create_pachimon_table      # 実装済み(下記の実際のSQLはmove_groupsへのFK無し・BIGINT型)
sqlx migrate add create_type_chart_table
sqlx migrate add create_player_pachimon_table
sqlx migrate add create_player_pachimon_moves_table
sqlx migrate add create_scout_banners_table
sqlx migrate add create_scout_pulls_table
sqlx migrate add create_battle_matches_table
sqlx migrate add create_battle_turns_table
```

`migrations/`配下に生成された各SQLファイルに、以下のCREATE TABLE文を記述する。

```sql
-- create_devices_table
CREATE TABLE devices (
    device_id CHAR(26) PRIMARY KEY,
    secret_key_hash VARCHAR(255) NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
);
```

```sql
-- create_access_tokens_table
CREATE TABLE access_tokens (
    device_id CHAR(26) PRIMARY KEY,
    access_token VARCHAR(64) NOT NULL UNIQUE,
    expires_at DATETIME(3) NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);
```

```sql
-- create_players_table
CREATE TABLE players (
    player_id CHAR(26) PRIMARY KEY,
    device_id CHAR(26) NOT NULL UNIQUE,
    nickname VARCHAR(50) NOT NULL,
    gems INT NOT NULL DEFAULT 0,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);
```

```sql
-- create_messages_table
CREATE TABLE messages (
    message_id CHAR(26) PRIMARY KEY,
    player_id CHAR(26) NOT NULL,
    content TEXT NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (player_id) REFERENCES players(player_id)
);

CREATE INDEX idx_messages_created_at ON messages(created_at);
```

```sql
-- create_move_groups_table
CREATE TABLE move_groups (
    move_group_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);
```

```sql
-- create_moves_table
CREATE TABLE moves (
    move_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    type VARCHAR(20) NOT NULL,
    category ENUM('physical', 'special', 'status') NOT NULL,
    base_power INT NULL,
    accuracy INT NOT NULL,
    max_pp INT NOT NULL
);
```

```sql
-- create_move_group_moves_table
CREATE TABLE move_group_moves (
    move_group_id INT NOT NULL,
    move_id INT NOT NULL,
    is_initial BOOLEAN NOT NULL DEFAULT FALSE,
    UNIQUE (move_group_id, move_id),
    FOREIGN KEY (move_group_id) REFERENCES move_groups(move_group_id),
    FOREIGN KEY (move_id) REFERENCES moves(move_id)
);
```

```sql
-- create_pachimon_table(実装済み。move_groups未実装のためFK無し)
CREATE TABLE pachimon (
    pachimon_id BIGINT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    primary_type BIGINT NOT NULL,
    secondary_type BIGINT NOT NULL,
    base_hp BIGINT NOT NULL,
    base_atk BIGINT NOT NULL,
    base_def BIGINT NOT NULL,
    base_spatk BIGINT NOT NULL,
    base_spdef BIGINT NOT NULL,
    base_speed BIGINT NOT NULL,
    rarity BIGINT NOT NULL,
    move_group_id BIGINT NOT NULL
);
```

```sql
-- shrink_pachimon_enum_columns(実装済み)
-- primary_type/secondary_type/rarityは値域(0-18 / 1-4)に対してBIGINTが過大なため、
-- TINYINT UNSIGNEDへ変更する。
ALTER TABLE pachimon
    MODIFY COLUMN primary_type TINYINT UNSIGNED NOT NULL,
    MODIFY COLUMN secondary_type TINYINT UNSIGNED NOT NULL,
    MODIFY COLUMN rarity TINYINT UNSIGNED NOT NULL;
```

> `move_groups`実装時に、`ALTER TABLE pachimon ADD FOREIGN KEY (move_group_id) REFERENCES move_groups(move_group_id);`相当のマイグレーションを追加する想定。

```sql
-- create_type_chart_table
CREATE TABLE type_chart (
    attack_type VARCHAR(20) NOT NULL,
    defend_type VARCHAR(20) NOT NULL,
    multiplier DECIMAL(3, 2) NOT NULL,
    PRIMARY KEY (attack_type, defend_type)
);
```

```sql
-- create_player_pachimon_table
CREATE TABLE player_pachimon (
    player_pachimon_id CHAR(26) PRIMARY KEY,
    player_id CHAR(26) NOT NULL,
    pachimon_id INT NOT NULL,
    level INT NOT NULL,
    ivs JSON NOT NULL,
    party_slot INT NULL,
    effort_values JSON NOT NULL,
    obtained_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    UNIQUE (player_id, party_slot),
    FOREIGN KEY (player_id) REFERENCES players(player_id),
    FOREIGN KEY (pachimon_id) REFERENCES pachimon(pachimon_id)
);
```

```sql
-- create_player_pachimon_moves_table
CREATE TABLE player_pachimon_moves (
    player_pachimon_id CHAR(26) NOT NULL,
    slot INT NOT NULL,
    move_id INT NOT NULL,
    UNIQUE (player_pachimon_id, slot),
    FOREIGN KEY (player_pachimon_id) REFERENCES player_pachimon(player_pachimon_id),
    FOREIGN KEY (move_id) REFERENCES moves(move_id)
);
```

```sql
-- create_scout_banners_table
CREATE TABLE scout_banners (
    banner_id CHAR(26) PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    rate_table JSON NOT NULL,
    cost_per_pull INT NOT NULL,
    start_at DATETIME(3) NOT NULL,
    end_at DATETIME(3) NOT NULL
);
```

```sql
-- create_scout_pulls_table
CREATE TABLE scout_pulls (
    pull_id CHAR(26) PRIMARY KEY,
    player_id CHAR(26) NOT NULL,
    banner_id CHAR(26) NOT NULL,
    pachimon_id INT NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (player_id) REFERENCES players(player_id),
    FOREIGN KEY (banner_id) REFERENCES scout_banners(banner_id),
    FOREIGN KEY (pachimon_id) REFERENCES pachimon(pachimon_id)
);
```

```sql
-- create_battle_matches_table
CREATE TABLE battle_matches (
    match_id CHAR(26) PRIMARY KEY,
    player1_id CHAR(26) NOT NULL,
    player2_id CHAR(26) NOT NULL,
    status ENUM('matching', 'in_progress', 'finished') NOT NULL,
    winner_id CHAR(26) NULL,
    player1_selected_pachimon JSON NULL,
    player2_selected_pachimon JSON NULL,
    started_at DATETIME(3) NULL,
    ended_at DATETIME(3) NULL,
    FOREIGN KEY (player1_id) REFERENCES players(player_id),
    FOREIGN KEY (player2_id) REFERENCES players(player_id),
    FOREIGN KEY (winner_id) REFERENCES players(player_id)
);
```

```sql
-- create_battle_turns_table
CREATE TABLE battle_turns (
    turn_id CHAR(26) PRIMARY KEY,
    match_id CHAR(26) NOT NULL,
    turn_number INT NOT NULL,
    player_id CHAR(26) NOT NULL,
    action_data JSON NOT NULL,
    result_data JSON NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    FOREIGN KEY (match_id) REFERENCES battle_matches(match_id),
    FOREIGN KEY (player_id) REFERENCES players(player_id)
);
```

マイグレーションを実行し、テーブルを反映する(`.env`の`DATABASE_URL`を参照する)。

```bash
sqlx migrate run
```

反映されたか、MySQLに接続して確認する。

```bash
make mysql
```

```sql
SHOW TABLES;
```

`devices`, `access_tokens`, `players`, `messages`, `move_groups`, `moves`, `move_group_moves`, `pachimon`, `type_chart`, `player_pachimon`, `player_pachimon_moves`, `scout_banners`, `scout_pulls`, `battle_matches`, `battle_turns`の15テーブルが表示されれば成功。`migrations/`ディレクトリはリポジトリにコミットする対象(Gitで管理する)。

## 未確定の論点

- パーティ編成のレベル制限は将来拡張として保留
- 努力値(64ポイント)の自由配分機能とその上限チェックは未実装(`effort_values`カラムは用意済み)
- 技の入れ替えUI・選択ロジックの高度化(候補が5件以上ある場合の絞り込み等)はスキーマのみ対応済み
