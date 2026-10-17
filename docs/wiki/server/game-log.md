---
title: "ゲームログ"
status: current
area: server
related: [server/structure, server/internal-api, db/index, game/gems, conventions/items]
proto: []
code: [Server/src/service/game_log.rs, Server/src/service/player_item_service.rs, Server/src/service/player_pachimon_service.rs, Server/src/service/player_service.rs, Server/src/service/scout_service.rs, Server/src/service/battle_service.rs, Server/src/grpc/internal_battle.rs, Server/src/config.rs]
updated: 2026-10-17
---

# ゲームログ

問い合わせ(「ジェムが減った」)・補填・不正の調査のために、所持データの変化を基盤(VolTech gRPC)のゲームログで残す。
サーバーの動きを追う運用ログ([APIサーバーの構成](structure.md)の「ログ」)とは別の種類。

## 出力先

`GAME_LOG_SINK` で選ぶ([開発環境ガイド](../development.md))。クライアント向けの API と内部 API で同じ出力先を使う。

| 値 | 出力先 | 使う場面 |
|---|---|---|
| `database`(既定) | 基盤の `game_logs` テーブル。業務の処理と同じトランザクションで書く | ログの分析の基盤がまだ無いので、今はこれ |
| `stdout` | 標準出力の JSON(`log_type: game`。`RUST_LOG` で消えない) | AWS などに載せて、ログの収集・分析のサービスへ送るとき。アプリのコードは変えない |

- どちらも、コミットしたときだけ残る(ロールバック・再送で保存済みのレスポンスを返したとき・報告済みで断った結果の報告では残らない)
- `player_id`・`request_id`・RPC の名前・時刻は基盤が付ける。名前は書かず、ID だけを書く

## 残しているもの

数量の変化(アイテム)は基盤の「種別・対象の ID・増減・更新後の値・理由」の形で、それ以外は「種別・対象の ID・理由」に名前付きの値を足した形で残す。
`database` では名前付きの値は `body` の JSON に入る。

| 種別(`kind`) | 対象の ID | 値 | 理由(`reason`) |
|---|---|---|---|
| `item`(アイテムの増減) | アイテムの ID | 増減(取得は正、消費は負)・更新後の所持数 | `sign_up`(初期の付与)・`scout_roll`・`battle_reward`(対戦の勝利の報酬)・`debug`(開発用の API) |
| `pachimon`(パチモンの入手) | 個体の ID | `pachimon_id`(種族)・`move_ids`(入手した時点の技。枠の順) | `sign_up`(初期パーティ)・`scout_select`・`debug`(ランダム編成) |
| `party`(パーティの変更) | なし | `slots`(変更後の全ての枠の `slot`・`player_pachimon_id`) | `sign_up`(初期パーティ)・`set_party`・`debug`(ランダム編成) |
| `pachimon_move`(技の付け替え) | 個体の ID | `slot`・`before_move_id`(空いていた枠なら `null`)・`after_move_id` | `set_pachimon_move` |
| `battle`(対戦の結果) | 対戦の ID | `opponent_id`・`result`(`win`・`lose`・`no_winner`) | `battle_result` |

- 所持データを変えるサービス層の関数が `GameLogCollector` に記録する。サービス層の関数は普通のトランザクションを受け取るので、
  内部 API やテストからも同じ関数を呼べる
  - クライアント向けのハンドラーは、処理の最後に基盤の `TxContext` へ書く(持ち主は認証したプレイヤー)
  - 内部 API の `ReportResult` は基盤の `Transactions::run` を通らないので、記録した分を基盤の `GameLogSink::commit` で書いてコミットする。
    1回の報告で2人の所持データが変わるので、`GameLogCollector` は持ち主ごとに記録する
- パーティは変更後の全ての枠を残す。編成は全置き換えなので、前の編成は1つ前の `party` のゲームログを見れば分かる
- 対戦の結果は、両方の参加者に、それぞれから見た結果で残す(相手のゲームログを探さなくても、そのプレイヤーの履歴だけで分かるように)。
  参加者は報告の順ではなく `battle_matches` の参加者で決める(一度も接続しなかった側にも残す)
- 内部 API の `method` は `atlas.internal.v1.BattleInternalService/ReportResult`。基盤の `RpcMethodLayer` を付けていないので、名前を自分で持つ。
  `request_id` はリクエストに `x-request-id` があれば付く(今のバトルサーバーは送らない)
- サインアップの初期の付与は、プレイヤーを作ったとき(作成済みでないとき)だけ残る
- [後始末](stale-match-cleanup.md)で打ち切った対戦は所持データを変えないので残さない

ショップ・技の付け替え・ステータスポイントでの育成チケットの消費は、機能を作るときに `ItemReason` を足して残す(アイテムの増減の形に合う)。
