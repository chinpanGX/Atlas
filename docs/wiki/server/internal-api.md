---
title: "内部API(バトルサーバー向け)"
status: current
area: server
related: [server/auth, server/stale-match-cleanup, battle-server/api-integration, db/battle-tables]
proto: [Shared/proto/atlas/internal/v1/battle.proto]
code: [Server/src/grpc/internal_battle.rs, Server/src/service/battle_service.rs, Server/src/service/player_pachimon_service.rs, Server/src/grpc.rs]
updated: 2026-10-18
---

# 内部API(バトルサーバー向け)

`atlas.internal.v1.BattleInternalService`。バトルサーバーから APIサーバーを呼ぶための gRPC の API で、Unity クライアントからは呼ばない。

- クライアント向けとは別のポート(`SERVER_ADDR`、既定 `127.0.0.1:3000`)で出す
- メタデータ `x-internal-secret` で認証する([認証](auth.md))
- 基盤の認証・冪等性・メンテナンスの判定は通らない。メンテナンス中でも結果の報告を受ける
- 業務エラー(`ErrorInfo`)は使わず、gRPC の標準のステータスで返す

## GetParty

`player_id` → `pachimon`(`player_pachimon_id`・`pachimon_id`)

- バトルサーバーが参加を受け付けたときに呼ぶ。パーティの枠番号の順(未編成の枠は含まないため0〜6件)
- ボットとの対戦では、ボットの分もこれで取る。ボットも `players` の普通のプレイヤーなので、`GetLoadouts`・`ReportResult` も含めて
  人と同じに扱う([マッチング](matchmaking.md))
- 選出画面で見せるのはどのパチモンかだけなので、ステータスポイント・技は返さない

## GetLoadouts

`player_id`・`player_pachimon_ids` → `loadouts`(`player_pachimon_id`・`pachimon_id`・`effort_values`・`move_ids`)

- バトルサーバーが選出を受け取ったときに呼ぶ。順番はリクエストの `player_pachimon_ids` と同じ、`move_ids` は技の枠の順
- プレイヤーごとに違う所持データだけを返し、種族値・技の性能・タイプ相性はバトルサーバーが自分のマスタから引く
- 所持していない個体(他人の個体・存在しない ID)が1体でもあれば `NOT_FOUND`

## ReportResult

`match_id`・`winner_id`・`player1_id`・`player2_id`・`player1_selected_pachimon`・`player2_selected_pachimon`・`turns`
(`turn_number`・`player_id`・`action_data_json`・`result_data_json`)→ 本文なし

1つのトランザクションで次を行う。

1. `battle_matches` の行を `FOR UPDATE` でロックし、報告済みかを確かめる
2. `battle_matches` を終了状態に更新する(勝者ありなら `finished`、`winner_id` が空文字なら `aborted`)
3. `battle_turns` に行動を一括で追加する
4. 勝者にジェム50を付与する(勝者なしなら付与しない)
5. 両方の参加者の対戦の結果と、勝者の報酬を[ゲームログ](game-log.md)に書く

記録できたら、マッチングに残っている両者の接続情報を消す([マッチング](matchmaking.md))。

| 状況 | 返すステータス |
|---|---|
| 対戦が無い | `NOT_FOUND` |
| 参加者でない `winner_id`・`player1_id`/`player2_id`・ターンの `player_id`、2人が同じ ID、JSON として読めない行動・結果 | `INVALID_ARGUMENT` |
| 報告済み | `ALREADY_EXISTS`(何も変えない。ジェムを二重に付与しない) |

- `player1`/`player2` はバトルサーバーに先に参加した側で、マッチ成立時の順と一致するとは限らない。ID で `battle_matches` の参加者と
  突き合わせて、選出を正しい列に保存する
- 相手が一度も参加しなかった場合、その側の ID は空文字・選出は空
- 行動と結果は中身を解釈せず JSON のまま保存する(HP の実数など、クライアントに送らない値も含む)。JSON として読めないものだけ拒否する
- 報告済みかどうかは選出の列が `NULL` でないかで判定する。結果報告は必ず選出の列を設定し、[後始末](stale-match-cleanup.md) は
  設定しないため、後始末で `aborted` になった対戦に後から届いた報告は受け付け、内容で上書きする

## 採用しなかった案

- バトルサーバーが MySQL を直接読む: DB へのアクセスを APIサーバーにまとめる方針から外れるため
- マッチ成立時に `battle_token` へパーティ全員分の所持データを入れる: 使わない個体の分までトークンに載り、トークンの役割
  (参加資格の証明)とも混ざるため
