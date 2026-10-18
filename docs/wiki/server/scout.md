---
title: "スカウト(API)"
status: current
area: server
related: [features/scout, db/index, conventions/items, client/screens]
proto: [Shared/proto/atlas/v1/scout.proto]
code: [Server/src/grpc/scout.rs, Server/src/service/scout_service.rs, Server/src/master/scout_banner.rs, Shared/master-data/schema/tables/scout_banners.yaml, Shared/master-data/schema/tables/scout_banner_rates.yaml]
updated: 2026-10-19
---

# スカウト(API)

本家の「紹介(候補10体から1体を選ぶ)」をもとにした2段階の流れ。

## ScoutService/ListBanners

本文なし → `banners`(`banner_id`・`name`・`cost_per_roll`・`start_at`・`end_at`)

- 開催中(`start_at <= 現在 <= end_at`、UTC)のバナーを開始日時の順に返す。排出率は返さない
- バナーは起動時に読み込んだマスタ(`MasterData`)から引き、DB には問い合わせない

## ScoutService/Roll

`banner_id` → `roll_id`・`candidates`(`index`・`pachimon_id`・`move_ids`)・`player_diff`

1. マスタにバナーがあるか確かめる(無ければ `SCOUT_BANNER_NOT_FOUND`、開催期間外は `SCOUT_BANNER_CLOSED`)
2. ジェムから `cost_per_roll` を減らす(足りなければ `INSUFFICIENT_GEMS`)
3. 候補10体を1体ずつ独立に抽選する
   1. バナーの排出率(`scout_banner_rates` の `weight`。1万分率)の重みでレア度を決める
   2. そのレア度の種族から等確率で1体選ぶ
   3. 技はその種族の初期技(`move_group_moves.is_initial`)4つ
4. 候補10体を `scout_rolls` に保存する(未選択)

以上を1つのトランザクションで行う。ジェムの消費はこの時点で確定し、候補を選ばなくても戻らない。ジェムの消費はゲームログ(理由 `scout_roll`)に残す。

## ScoutService/Select

`roll_id`・`index` → `player_diff`

- 検証の順: 自分のロールであること(無い・他人のロールは `SCOUT_ROLL_NOT_FOUND`)、未選択であること(選択済みは
  `SCOUT_ROLL_ALREADY_SELECTED`)、`index` が候補の範囲(0〜9)であること(範囲外は `SCOUT_CANDIDATE_OUT_OF_RANGE`)
- 同時に選んでも二重に入手しないよう、ロールの行を `FOR UPDATE` でロックしてから選択済みかを見る
- 保存しておいた候補をそのまま `player_pachimon`・`player_pachimon_moves` にコピーする(再抽選しない)。選ばなかった9体はどこにも残らない
- ジェムはここでは変わらない(紹介の時点で消費済み)

## 設計上の判断

- バナー(`scout_banners`)と排出率(`scout_banner_rates`)は、ほかのマスタと同じくマスターデータにし、APIサーバーにだけ出力する
  (`targets: [server]`。排出率をクライアントに配らない)。開催中のバナーはクライアントに `ListBanners` で返すので、クライアントを作り直さずに切り替えられる
- 反映のしかたもほかのマスタと同じ: `seed_master_data` で DB へ投入し、サーバーを再起動して `MasterData` に読み込み直す。
  開催中かはリクエストのたびに今の時刻と比べるので、開催期間の始まり・終わりで再起動は要らない
- 常設の「レギュラースカウト」(`SCOUT000000000000000000001`。150ジェム、S 3%・A 12%・B 35%・C 50%、終了日時は2126年)を1件持つ。
  `banner_id` は `scout_rolls` が外部キーで参照するので、一度使った値は変えない
- パイプラインの `validate` で確かめられない決まり(日時として読めること、`start_at < end_at`、バナーごとの重みの合計が10000、重みが負でないこと、同じバナーに同じレア度の行が2つ無いこと)は、
  `seed_master_data` が何かを投入する前に確かめ、合わなければ投入を止める
- 候補と選択結果を `scout_rolls` にまとめて記録し、紹介と入手の履歴を兼ねる
- 紹介を選ばずに放置してもジェムは戻らず、ロールの有効期限も設けない。取り直す API が無いため、クライアントは候補を選ぶまで
  画面を離れられないようにしている
