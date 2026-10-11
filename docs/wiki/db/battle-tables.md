---
title: "対戦のテーブル"
status: current
area: db
related: [db/index, server/matchmaking, server/internal-api, server/stale-match-cleanup]
code: [Server/migrations, Server/src/service/battle_service.rs, BattleServer/Internal/BattleResultReporter.cs]
updated: 2026-10-11
---

# 対戦のテーブル

## battle_matches

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `match_id` | CHAR(26) | PK | |
| `player1_id` / `player2_id` | CHAR(26) | NOT NULL, FK → `players` | `player1` は先に待機列にいた側 |
| `status` | ENUM('matching','in_progress','finished','aborted') | NOT NULL | マッチ成立時に `in_progress` で作る(`matching` は使わない)。`aborted` は勝者なしの終了 |
| `winner_id` | CHAR(26) | NULL 可, FK → `players` | `finished` のときだけ |
| `player1_selected_pachimon` / `player2_selected_pachimon` | JSON | NULL 可 | 選出した `player_pachimon_id`(選んだ順)。結果報告で必ず設定するため、`NULL` なら未報告 |
| `started_at` / `ended_at` | DATETIME(3) | NULL 可 | |

状態の移り変わり:

```
(マッチ成立) → in_progress ─┬─ 結果報告(勝者あり)          → finished
                            ├─ 結果報告(勝者なし)          → aborted
                            └─ 1時間報告が無い(後始末)    → aborted ──(後から報告)→ finished / aborted
```

## battle_turns

| 列 | 型 | 制約 | 説明 |
|---|---|---|---|
| `turn_id` | CHAR(26) | PK | |
| `match_id` | CHAR(26) | NOT NULL, FK → `battle_matches` | |
| `turn_number` | INT | NOT NULL | |
| `player_id` | CHAR(26) | NOT NULL, FK → `players` | 行動した側 |
| `action_data` | JSON | NOT NULL | `{"type": "Move"/"Switch"/"Skip", "moveId", "partySlot"}` |
| `result_data` | JSON | NOT NULL | `{"hit","critical","effectiveness","damageDealt","targetRemainingHp","targetFainted","newActiveIndex"}` |
| `created_at` | DATETIME(3) | NOT NULL | |

- 1ターン内の行動ごとに1行
- `action_data`・`result_data` はバトルサーバーが作り、APIサーバーは解釈せずに保存する。HP の実数やダメージ量など、
  クライアントには送らない値も含む
- 報酬の付与履歴のテーブルは持たない(`winner_id` と固定額から計算し直せるため)
- バトルサーバーの ID は持たない(1台構成のため)
