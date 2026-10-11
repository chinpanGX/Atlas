---
title: "スカウト(API)"
status: current
area: server
related: [game/scout, db/scout-tables, conventions/items, client/screens]
proto: [Shared/proto/atlas/v1/scout.proto]
code: [Server/src/grpc/scout.rs, Server/src/service/scout_service.rs, Server/src/bin/seed_scout_banners.rs]
updated: 2026-10-11
---

# スカウト(API)

本家の「紹介(候補10体から1体を選ぶ)」をもとにした2段階の流れ。

## ScoutService/ListBanners

本文なし → `banners`(`banner_id`・`name`・`cost_per_roll`・`start_at`・`end_at`)

- 開催中(`start_at <= 現在 <= end_at`)のバナーを開始日時の順に返す。排出率は返さない

## ScoutService/Roll

`banner_id` → `roll_id`・`candidates`(`index`・`pachimon_id`・`move_ids`)・`player_diff`

1. バナーがあるか確かめる(無ければ `SCOUT_BANNER_NOT_FOUND`、開催期間外は `SCOUT_BANNER_CLOSED`)
2. ジェムから `cost_per_roll` を減らす(足りなければ `INSUFFICIENT_GEMS`)
3. 候補10体を1体ずつ独立に抽選する
   1. バナーの `rate_table` の重みでレア度を決める
   2. そのレア度の種族から等確率で1体選ぶ
   3. 技はその種族の初期技(`move_group_moves.is_initial`)4つ
4. 候補10体を `scout_rolls` に保存する(未選択)

以上を1つのトランザクションで行う。ジェムの消費はこの時点で確定し、候補を選ばなくても戻らない。

## ScoutService/Select

`roll_id`・`index` → `player_diff`

- 検証の順: 自分のロールであること(無い・他人のロールは `SCOUT_ROLL_NOT_FOUND`)、未選択であること(選択済みは
  `SCOUT_ROLL_ALREADY_SELECTED`)、`index` が候補の範囲(0〜9)であること(範囲外は `SCOUT_CANDIDATE_OUT_OF_RANGE`)
- 同時に選んでも二重に入手しないよう、ロールの行を `FOR UPDATE` でロックしてから選択済みかを見る
- 保存しておいた候補をそのまま `player_pachimon`・`player_pachimon_moves` にコピーする(再抽選しない)。選ばなかった9体はどこにも残らない
- ジェムはここでは変わらない(紹介の時点で消費済み)

## 設計上の判断

- バナーはマスターデータ(`master-data-pipeline`)の対象外にし、APIサーバーの DB にだけ置く。開催期間を持つ運用寄りのデータで、
  クライアントを作り直さずに切り替えたいことと、排出率をクライアントに渡さないため
- 投入は専用の seed(`cargo run --bin seed_scout_banners`)。常設の「レギュラースカウト」(150ジェム、S 3%・A 12%・B 35%・C 50%、
  終了日時は100年後)を1件入れる。期間限定バナーの運用ツールは作らない
- 候補と選択結果を `scout_rolls` にまとめて記録し、紹介と入手の履歴を兼ねる
- 紹介を選ばずに放置してもジェムは戻らず、ロールの有効期限も設けない。取り直す API が無いため、クライアントは候補を選ぶまで
  画面を離れられないようにしている
