---
title: "プレイヤー(サインアップ・サインイン)"
status: current
area: server
related: [game/flow, client/sign-in, conventions/player-diff, db/player-tables, server/game-log]
proto: [Shared/proto/atlas/v1/player.proto, Shared/proto/atlas/v1/player_data.proto]
code: [Server/src/grpc/player.rs, Server/src/service/player_service.rs, Server/src/service/player_pachimon_service.rs]
updated: 2026-10-18
---

# プレイヤー(サインアップ・サインイン)

## PlayerService/SignUp

`nickname` → `player_id`・`nickname`

- デバイス登録で発行した `player_id` でプレイヤーを作る
- 作成済みならエラーにせず、何も変えずに作成済みのプレイヤーを返す。再送されても同じ結果になるよう `IDEMPOTENT` にしている。
  同じプレイヤーのサインアップが同時に届いても片方だけが作るよう、`players` の行を `FOR UPDATE` で確かめてから作る
- ニックネームは1〜50文字(範囲外は `INVALID_ARGUMENT`。`players.nickname` の `VARCHAR(50)` に合わせた)。作成済みのときは使わない
- プレイヤーの作成と同じトランザクションで次を付与する。途中で失敗しても「プレイヤーはいるがパーティが空」にはならない
  - スターターパーティ: マスタ `starter_party_slots` の6体を複製し、初期技込みの `player_pachimon`・`player_pachimon_moves` と
    `player_party_slots` を作る(全員共通の固定編成)
  - ジェム 300(`player_items`。スカウト2回分で、初回起動の時点でスカウトを試せるようにする)
  - 基盤の冪等性のためにロックする行(`last_responses`)
- 付与したもの(パチモン・パーティ・ジェム)は理由 `sign_up` で[ゲームログ](game-log.md)に残す
- 作成したデータは返さない。クライアントは続けて `SignIn` を呼ぶ

## PlayerService/SignIn

本文なし → `player_id`・`nickname`・`snapshot`(所持データの全件)

- タイトル画面で毎回呼び、所持データの全件(アイテム・個体(ステータスポイントの割り振りを含む)・技・パーティ)を返す
- クライアントは差分としてではなく、手元のデータを置き換える。そのため `player_diff` ではなく `snapshot` という名前で返す
  ([所持データの差分](../conventions/player-diff.md))
- プレイヤーが未作成なら `FAILED_PRECONDITION` + `PLAYER_NOT_FOUND`。クライアントはこのときだけ `SignUp` を呼んでから、
  もう一度サインインする(サインアップ済みかは端末に覚えない)
