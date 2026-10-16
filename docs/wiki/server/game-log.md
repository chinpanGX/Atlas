---
title: "ゲームログ"
status: current
area: server
related: [server/structure, db/index, game/gems, conventions/items]
proto: []
code: [Server/src/service/game_log.rs, Server/src/service/player_item_service.rs, Server/src/config.rs]
updated: 2026-10-16
---

# ゲームログ

問い合わせ(「ジェムが減った」)・補填・不正の調査のために、所持データの変化を基盤(VolTech gRPC)のゲームログで残す。
サーバーの動きを追う運用ログ([APIサーバーの構成](structure.md)の「ログ」)とは別の種類。

## 出力先

`GAME_LOG_SINK` で選ぶ([開発環境ガイド](../development.md))。

| 値 | 出力先 | 使う場面 |
|---|---|---|
| `database`(既定) | 基盤の `game_logs` テーブル。業務の処理と同じトランザクションで書く | ログの分析の基盤がまだ無いので、今はこれ |
| `stdout` | 標準出力の JSON(`log_type: game`。`RUST_LOG` で消えない) | AWS などに載せて、ログの収集・分析のサービスへ送るとき。アプリのコードは変えない |

- どちらも、コミットしたときだけ残る(ロールバック・再送で保存済みのレスポンスを返したときは残らない)
- `player_id`・`request_id`・RPC の名前・時刻は基盤が付ける。名前は書かず、ID だけを書く

## 残しているもの

基盤のゲームログの形は「種別・対象の ID・増減・更新後の値・理由」。

| 種別 | 対象の ID | 増減 | 更新後の値 | 理由 |
|---|---|---|---|---|
| `item`(アイテムの増減) | アイテムの ID | 取得は正、消費は負 | 更新後の所持数 | `sign_up`(初期の付与)・`scout_roll`・`debug`(開発用の API) |

- アイテムを増減する関数(`player_item_service` の `grant_initial`・`add`・`deduct`)が `GameLogCollector` に記録し、
  ハンドラーが処理の最後に基盤の `TxContext` へ書く。サービス層の関数は普通のトランザクションを受け取るので、内部 API やテストからも同じ関数を呼べる
- サインアップの初期の付与は、プレイヤーを作ったとき(作成済みでないとき)だけ残る

## まだ残せないもの(基盤の対応待ち)

| もの | 残らない理由 |
|---|---|
| 対戦の勝利の報酬(ジェム) | 結果の報告は内部 API で、基盤の `Transactions::run` を通らない。ゲームログを書く口(`TxContext`)が無いので、記録した分を捨てている |
| 対戦の結果(対戦 ID・相手・結果) | 同上 |
| パチモンの入手(個体 ID・種族 ID・入手の理由) | 基盤のゲームログは数量の変化の形(増減と更新後の値)だけで、個体 ID と種族 ID を両方書く場所が無い |
| パーティの変更(変更後の枠と個体 ID) | 同上(数量の変化ではない) |
| 技の付け替え(個体 ID・枠・前後の技 ID) | 同上 |

ショップ・技の付け替え・ステータスポイントでの育成チケットの消費は、機能を作るときに `ItemReason` を足して残す(アイテムの増減の形に合う)。
