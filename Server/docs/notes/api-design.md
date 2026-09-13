# Atlas Server 設計書

## 概要

デバイス認証(ゲスト型アカウント)とシンプルなチャット機能を提供するAPIサーバー。サインアップ/サインイン(会員登録)は学習用途では対象外とし、`device_id`単位でのアカウント管理のみを行う。

## API仕様

| # | メソッド | パス | 説明 |
|---|---|---|---|
| 1 | POST | `/devices` | デバイス新規登録 |
| 2 | POST | `/devices/authenticate` | デバイス認証・アクセストークン取得 |
| 3 | GET | `/auth/verify` | アクセストークン検証 |
| 4 | POST | `/chat/send` | メッセージ送信 |
| 5 | GET | `/chat/poll` | メッセージ受信/取得 |

### 1. デバイス新規登録

```
POST /devices
```

リクエスト
```json
{ "secret_key": "client-side-generated-random-string" }
```

レスポンス
```json
{ "device_id": "my-device-id", "player_id": "my-player-id" }
```

### 2. デバイス認証

```
POST /devices/authenticate
```

リクエスト
```json
{ "device_id": "my-device-id", "secret_key": "my-secret-key" }
```

レスポンス
```json
{ "access_token": "my-access-token", "expires_in": 360 }
```

設計方針:1つの`device_id`につき有効なアクセストークンは常に1つのみ。再認証時は古いトークンを新しいトークンで上書き(無効化)する。多くのモバイルゲームでも、同一アカウントの多重ログインは元々想定されない/意図的に弾く実装が一般的なため、この方針で問題ない。

> 補足(クライアント実装時の考慮事項):本設計では別途「リフレッシュトークン」を用意せず、`device_id` + `secret_key`による`/devices/authenticate`の再実行がリフレッシュを兼ねる。クライアント実装時は、事前に有効期限をチェックして早めに再認証する処理と、401が返ってきた場合に再認証してリトライする処理を併用するのが望ましい(実装は将来のクライアント側実装時の課題とする)。

### 3. アクセストークン検証

```
GET /auth/verify
```

有効であれば`200`、無効(期限切れ・存在しない)であれば`401`を返す。

### 4. チャット送信

```
POST /chat/send
```

チャットルームの概念はなし。全プレイヤー共通の1つのチャットに送信する。要認証(アクセストークン必須)。

### 5. チャット受信/取得

```
GET /chat/poll
```

全プレイヤー共通のメッセージ一覧を取得する。要認証(アクセストークン必須)。

## DB設計

`device_id` / `player_id` にはULID(Universally Unique Lexicographically Sortable Identifier)を採用する。UUIDと同様に衝突しにくいID体系でありながら、生成時刻順にソート可能・26文字固定長という特徴を持つ。Rustでは`ulid`クレートで生成できる。

### devices

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `device_id` | VARCHAR(26) | PRIMARY KEY | ULID |
| `player_id` | VARCHAR(26) | NOT NULL, UNIQUE | ULID |
| `secret_key_hash` | VARCHAR(255) | NOT NULL | `secret_key`をハッシュ化して保存 |
| `created_at` | DATETIME | NOT NULL, DEFAULT CURRENT_TIMESTAMP | 登録日時 |

### access_tokens

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `token` | VARCHAR(64) | PRIMARY KEY | ランダム生成した文字列 |
| `device_id` | VARCHAR(26) | NOT NULL, FOREIGN KEY → `devices.device_id`, UNIQUE | 1device_id = 1トークン。再認証時は上書き |
| `expires_at` | DATETIME | NOT NULL | 有効期限(発行時刻 + `expires_in`秒) |
| `created_at` | DATETIME | NOT NULL, DEFAULT CURRENT_TIMESTAMP | 発行日時 |

`device_id`にUNIQUE制約を付けることで、1デバイスにつき常に1レコード(1トークン)のみ存在する状態を保証する。再認証時はUPSERT(既存レコードがあれば更新)で対応する。

### messages

| カラム名 | 型 | 制約 | 説明 |
|---|---|---|---|
| `id` | BIGINT | PRIMARY KEY, AUTO_INCREMENT | メッセージID |
| `sender_player_id` | VARCHAR(26) | NOT NULL, FOREIGN KEY → `devices.player_id` | 送信者 |
| `content` | TEXT | NOT NULL | メッセージ本文 |
| `created_at` | DATETIME | NOT NULL, DEFAULT CURRENT_TIMESTAMP | 送信日時 |

`created_at`にインデックスを張り、`poll`時の新しい順取得を高速化する。

## マイグレーション手順

上記のテーブル定義を、`sqlx-cli`でマイグレーションファイルとして反映する(`sqlx-cli`自体の導入は`init.md`を参照)。

マイグレーションファイルの雛形を作成する。

```bash
sqlx migrate add create_devices_table
sqlx migrate add create_access_tokens_table
sqlx migrate add create_messages_table
```

`migrations/`配下に生成された各SQLファイルに、以下のCREATE TABLE文を記述する。

```sql
-- migrations/xxxxxxxxxxxxxx_create_devices_table.sql
CREATE TABLE devices (
    device_id VARCHAR(26) PRIMARY KEY,
    player_id VARCHAR(26) NOT NULL UNIQUE,
    secret_key_hash VARCHAR(255) NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
```

```sql
-- migrations/xxxxxxxxxxxxxx_create_access_tokens_table.sql
CREATE TABLE access_tokens (
    token VARCHAR(64) PRIMARY KEY,
    device_id VARCHAR(26) NOT NULL UNIQUE,
    expires_at DATETIME NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);
```

```sql
-- migrations/xxxxxxxxxxxxxx_create_messages_table.sql
CREATE TABLE messages (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    sender_player_id VARCHAR(26) NOT NULL,
    content TEXT NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (sender_player_id) REFERENCES devices(player_id)
);

CREATE INDEX idx_messages_created_at ON messages(created_at);
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

`devices`, `access_tokens`, `messages`の3つが表示されれば成功。`migrations/`ディレクトリはリポジトリにコミットする対象(Gitで管理する)。