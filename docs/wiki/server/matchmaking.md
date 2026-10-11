---
title: "マッチング"
status: current
area: server
related: [game/battle-rules, server/auth, server/internal-api, client/services-and-player-data, db/battle-tables]
proto: [Shared/proto/atlas/v1/matchmaking.proto]
code: [Server/src/grpc/matchmaking.rs, Server/src/service/matchmaking_service.rs, Server/src/service/battle_service.rs, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/BattleMatchMaker.cs]
updated: 2026-10-11
---

# マッチング

## MatchmakingService/JoinQueue

本文なし → 本文なし

- パーティが0体なら `PARTY_EMPTY`
- 既に待機中(またはペアを組んでいる最中)なら何もしない
- 前のマッチの接続情報が残っていれば捨ててから並ぶ
- 待っている人がいれば、その場で先頭の人と組む([成立](#成立))。いなければ待機列の末尾に入る

## MatchmakingService/LeaveQueue

待機列から抜け、成立していたマッチの接続情報も捨てる。待機列にいなくても成功する(`IDEMPOTENT`)。

## MatchmakingService/GetQueueStatus

本文なし → `status`・`match_id`・`battle_server`・`battle_token`

- 成立していなければ(待機列にいないときも含む)`QUEUE_STATUS_WAITING` で、ほかは空文字([値なしの表し方](../conventions/nullable.md))
- 成立していれば `QUEUE_STATUS_MATCHED` で、接続先(`battle_server`。環境変数 `BATTLE_SERVER_URL`、既定 `http://127.0.0.1:5000`)と
  `battle_token` を返す
- 成立の結果は返しても消さず、次の `JoinQueue`・`LeaveQueue` か、対戦の結果の報告([内部API](internal-api.md))で消す。
  応答が届かずに確かめ直しても接続情報を失わず、読むだけの RPC(`NO_SIDE_EFFECTS`)にできるため

## 待機列

DB に置かず、`AppState` のメモリに `Mutex<MatchmakingQueue>` で持つ(1台構成のため)。

| フィールド | 中身 |
|---|---|
| `waiting` | 待っている `player_id`(先頭ほど古い) |
| `matched` | `player_id` → 成立した結果(`match_id`・接続先・`battle_token`) |
| `pairing` | ペアは決まったが `battle_matches` への書き込み中の `player_id` |

ロックの中で `await` しないため `std::sync::Mutex` を使い、DB への書き込みはロックの外で行う。

## 成立

1. ロックの中で、待機列の先頭を相手に取り出し、2人を `pairing` に入れる
2. ロックの外で、`battle_matches` に `in_progress` の行を作り(先に待っていた側が `player1`)、2人分の `battle_token` を発行する
3. もう一度ロックを取り、2人を `pairing` から外す。成功なら2人分の結果を `matched` に入れる。失敗なら相手を待機列の先頭に戻してエラーにする

- 書き込み中の2人を `pairing` に入れておくのは、その間に同じプレイヤーが再参加して二重にマッチしないようにするため
- 失敗したら相手を戻すのは、DB に行が無い対戦をクライアントへ返さないため

## 通知はポーリング

クライアントは `JoinQueue` の後、1秒ごとに `GetQueueStatus` を呼んで成立を確かめる。1秒の遅れは体験に影響せず、
サーバーからの配信を入れる手間に見合わないため。キャンセルされたら `LeaveQueue` を呼ぶ(抜けられなくても、次に参加したときに
前の分は捨てられる)。クライアントの実装は [サービスと所持データ](../client/services-and-player-data.md)。
