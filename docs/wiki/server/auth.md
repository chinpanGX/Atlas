---
title: "認証"
status: current
area: server
related: [server/structure, server/matchmaking, server/internal-api, client/sign-in, battle-server/battle-flow]
code: [Server/src/grpc/player_lookup.rs, Server/src/grpc/context.rs, Server/src/grpc/internal_battle.rs, Server/src/service/matchmaking_service.rs, BattleServer/Auth/BattleTokenValidator.cs]
updated: 2026-10-11
---

# 認証

APIサーバーにかかわる認証は3種類ある。

| 種類 | 誰が | 何で | 確かめる場所 |
|---|---|---|---|
| デバイス認証(ゲスト) | Unity クライアント・BattleBot | アクセストークン(`authorization: Bearer`) | APIサーバー(基盤の `AuthLayer`) |
| `battle_token` | Unity クライアント・BattleBot | マッチ成立時に発行する JWT | バトルサーバー |
| 内部 API のシークレット | バトルサーバー | メタデータ `x-internal-secret` | APIサーバー(内部 API の interceptor) |

## デバイス認証(ゲスト)

基盤(`voltech-grpc-auth`)が行う。

- 会員登録は無く、端末ごとのデバイスで認証する。クライアントが `secret_key` を作って `DeviceService/Register` で登録し、
  `device_id` と `player_id` を受け取る。以後は `device_id` と `secret_key` で `DeviceService/IssueToken` を呼び、アクセストークンを受け取る。
  どちらもクライアントの基盤(`ApiCore`)が最初の通信で自動で行う
- アクセストークンの有効期限は1時間。1つのデバイスにつき有効なトークンは常に1つ
- `player_id` はデバイス登録で発行する ID だけで、プレイヤー(`players` の行と初期データ)はサインアップで作る。
  デバイスとプレイヤーの対応は基盤の `devices` だけが持ち、`players` は `device_id` を持たない
- サーバーがプレイヤーを特定する根拠はトークンだけ(`device_id`・`player_id` をリクエストから受け取らない)
- 基盤の `AuthLayer` がトークンを検証し、Atlas の `PlayerResolver` の実装(`PlayerLookup`)が `players` を引いて、
  プレイヤーが作成済みかを返す。アカウント停止は無いので `Suspended` は返さない
- ハンドラは `context::player_id` で作成済みのプレイヤーの ID を取り出す。未作成なら `PLAYER_NOT_FOUND`。
  サインアップだけは作成済みかを問わず、デバイス登録で発行した `player_id` を使う
- 基盤のテーブルの `player_id` は `BINARY(16)`、Atlas のテーブルは `CHAR(26)`。つなぎ目(`PlayerLookup`・`to_ulid`)で変換する

## battle_token

- マッチ成立時に APIサーバーが発行する HS256 の JWT。claims は `match_id`・`player_id`・`exp`。有効期限は30秒
  (マッチ成立直後にバトルサーバーへ接続するだけなので短くてよい)
- 共有シークレットは環境変数 `BATTLE_TOKEN_SECRET`(APIサーバーとバトルサーバーに同じ値)。バトルサーバーは32バイト未満なら起動しない
- バトルサーバーは検証の時刻のずれの許容を5秒にしている(既定の許容が30秒より大きいと、実質の有効期限が延びるため)
- 期限切れ(署名は正当)のトークンは無効と区別し、再接続のときに限り受け付ける([対戦の進行](../battle-server/battle-flow.md))

## 内部 API のシークレット

- バトルサーバーからの呼び出しは、メタデータ `x-internal-secret` のシークレットで認証する。定数時間で比較し、
  無い・違うときは `UNAUTHENTICATED`
- 共有値は両サーバーの環境変数 `INTERNAL_API_SECRET`。APIサーバーは未設定なら起動しない。バトルサーバーも、無いと対戦を
  始められないため未設定なら起動しない
