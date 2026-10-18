---
title: "マッチング"
status: current
area: server
related: [game/battle-rules, server/auth, server/internal-api, client/services-and-player-data, db/battle-tables, battle-server/battle-flow]
proto: [Shared/proto/atlas/v1/matchmaking.proto]
code: [Server/src/grpc/matchmaking.rs, Server/src/service/matchmaking_service.rs, Server/src/service/bot_player_service.rs, Server/src/service/battle_service.rs, Server/src/main.rs, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/BattleMatchMaker.cs]
updated: 2026-10-18
---

# マッチング

## MatchmakingService/JoinQueue

本文なし → 本文なし

- パーティが0体なら `PARTY_EMPTY`
- 既に待機中(またはペアを組んでいる最中)なら何もしない
- 前のマッチの接続情報が残っていれば捨ててから並ぶ
- 待っている人がいれば、その場で先頭の人と組む([成立](#成立))。いなければ待機列の末尾に入る
- 誰も来ないまま10秒待つと、ボットと組む([ボットとの対戦](#ボットとの対戦))

## MatchmakingService/LeaveQueue

待機列から抜け、成立していたマッチの接続情報も捨てる。待機列にいなくても成功する(`IDEMPOTENT`)。

## MatchmakingService/GetQueueStatus

本文なし → `status`・`match_id`・`battle_server`・`battle_token`

- 成立していなければ(待機列にいないときも含む)`QUEUE_STATUS_WAITING` で、ほかは空文字([値なしの表し方](../conventions/nullable.md))
- 成立していれば `QUEUE_STATUS_MATCHED` で、接続先(`battle_server`。環境変数 `BATTLE_SERVER_URL`、既定 `http://127.0.0.1:5000`)と
  `battle_token` を返す。相手が人かボットかで返す形は変わらない
- 成立の結果は返しても消さず、次の `JoinQueue`・`LeaveQueue` か、対戦の結果の報告([内部API](internal-api.md))で消す。
  応答が届かずに確かめ直しても接続情報を失わず、読むだけの RPC(`NO_SIDE_EFFECTS`)にできるため

## 待機列

DB に置かず、`AppState` のメモリに `Mutex<MatchmakingQueue>` で持つ(1台構成のため)。

| フィールド | 中身 |
|---|---|
| `waiting` | 待っているプレイヤー(`player_id` と待機列に入った時刻。先頭ほど古い) |
| `matched` | `player_id` → 成立した結果(`match_id`・接続先・`battle_token`) |
| `pairing` | ペアは決まったが `battle_matches` への書き込み中の `player_id` |

ロックの中で `await` しないため `std::sync::Mutex` を使い、DB への書き込みはロックの外で行う。

## 成立

1. ロックの中で、待機列の先頭を相手に取り出し、2人を `pairing` に入れる
2. ロックの外で、`battle_matches` に `in_progress` の行を作り(先に待っていた側が `player1`)、2人分の `battle_token` を発行する
3. もう一度ロックを取り、2人を `pairing` から外す。成功なら2人分の結果を `matched` に入れる。失敗なら相手を待機列の先頭に戻してエラーにする

- 書き込み中の2人を `pairing` に入れておくのは、その間に同じプレイヤーが再参加して二重にマッチしないようにするため
- 失敗したら相手を戻すのは、DB に行が無い対戦をクライアントへ返さないため

## ボットとの対戦

プレイヤーが少ない時間でも対戦を始められるよう、待機列で `BOT_MATCH_WAIT`(10秒)待っても相手が来なければ、ボットと組む。

- APIサーバーのバックグラウンドのタスク(`spawn_bot_matching`。`main.rs` から起動する)が1秒ごとに待機列を見て、待ち時間を過ぎた人を
  取り出す。組む手順は人どうしと同じで、書き込みの間は `pairing` に入れ、失敗したら待機列の先頭に戻す(次の確認で組み直す)
- `battle_matches` はプレイヤーを `player1`、ボットを `player2` として作る
- `battle_token` はプレイヤーの分だけ発行し、claims の `bot_player_id` にボットの `player_id` を入れる([認証](auth.md))。
  BattleServer はこれを見て、プレイヤーの参加と一緒にボットを参加させる([対戦の進行](../battle-server/battle-flow.md))
- `GetQueueStatus` を読むだけの RPC に保つため、ボットと組む処理は `GetQueueStatus` の中ではなく、別のタスクで行う

### ボットのプレイヤー

- ボットは `players` に行を持つ普通のプレイヤーで、`bot_players` に載せて区別する([対戦のテーブル](../db/battle-tables.md))。
  普通のプレイヤーにしたのは、`battle_matches` の外部キー・内部 API の `GetParty`・`GetLoadouts`・`ReportResult`・勝利報酬を、
  人との対戦と同じに使えるため
- デバイスを紐付けないので、クライアントからボットとしてサインインすることはできない
- `bot_players` が10人に届くまでは対戦のたびに新しく作り(`bot_player_service::pick`)、届いた後は10人からランダムに選ぶ。
  毎回作ると、対戦のたびにプレイヤーと所持データの行が増えていくため
- パーティは作ったときにマスタからランダムに組み(開発用 API の `RandomizeParty` と同じ処理)、その後は組み直さない。
  ボットの所持データの差分・ゲームログは残さない(返す先が無く、プレイヤーの行動でもないため)
- 勝利報酬は人との対戦と同じに付く(ボットが勝てばボットにも付く)

## 通知はポーリング

クライアントは `JoinQueue` の後、1秒ごとに `GetQueueStatus` を呼んで成立を確かめる。1秒の遅れは体験に影響せず、
サーバーからの配信を入れる手間に見合わないため。キャンセルされたら `LeaveQueue` を呼ぶ(抜けられなくても、次に参加したときに
前の分は捨てられる)。クライアントの実装は [サービスと所持データ](../client/services-and-player-data.md)。

## 採用しなかった案

- **ボットを別プロセスで常駐させる**(開発用の `BattleBot` を常に起動しておき、待ち時間を過ぎた人と組ませる): 起動し忘れると相手が
  いなくなるうえ、`BattleBot` はパーティを開発用 API(本番では無効)で組むため、そのままでは本番で動かない。
  プロセスの監視・配布も増える
- **ボットを BattleServer から自分自身へ MagicOnion で接続させる**: 対戦の進行はボットを知らずに済むが、ボットのトークンの発行・接続の
  失敗や切断の扱い・寿命の管理が BattleServer の中に増える。参加者の送り先を共通にする形で、進行の分岐は無くせたため採らなかった
