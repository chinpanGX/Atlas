---
title: "所持データの差分(player_diff)"
status: current
area: conventions
related: [client/services-and-player-data, server/structure, conventions/items]
proto: [Shared/proto/atlas/v1/player_data.proto]
code: [Server/src/service/player_diff.rs, Server/src/grpc/player_data.rs, Client/AtlasUnityProject/Assets/Scripts/Infrastructure/Grpc/PlayerDiff]
updated: 2026-10-18
---

# 所持データの差分(player_diff)

プレイヤーの所持データ(アイテム・パチモン・覚えている技・パーティ)を変える API は、レスポンスに
`player_diff`(`PlayerDiff`)を含める。クライアントは受け取った差分を手元のデータに反映する。

| 種別 | 中身 | クライアントの扱い |
|---|---|---|
| 通常のレスポンス | その画面の表示に使う一時的なデータ(スカウトの候補など) | 表示したら捨てる |
| `player_diff` | クライアントが持ち続ける所持データの差分 | 手元のデータに反映する |

レスポンスの形を API ごとに変えると、クライアントの反映処理が API ごとにばらばらになるため、共通の形にした。
`player_diff` は gRPC 基盤の「共通レスポンス項目」で、クライアントでは基盤の `ApiCore` が、成功したレスポンスを呼び出し側に
返す前に自動で反映する。

## 形

```
PlayerDiff
  items             { upserted: [PlayerItem],         removed: [item_id] }
  pachimon          { upserted: [PlayerPachimon],     removed: [player_pachimon_id] }
  pachimon_move_map { upserted: [PlayerPachimonMove], removed: [player_pachimon_move_id] }
  party_slots       { upserted: [PartySlot],          removed: [party_slot_id] }
```

- リソースの種別ごとに `upserted`(追加・更新した行)と `removed`(削除した行の ID)を持つ。変化が無い種別は `null`
- 値は変化量ではなく変更後の値(ジェムなら残数)。同じ差分を2回反映しても結果は変わらない
- 各種別は DB のテーブルに1対1で対応する(`player_items` / `player_pachimon` / `player_pachimon_moves` / `player_party_slots`)
- 技をパチモンに内包させないのは、技の付け替えで、変化していないパチモン本体まで送り直さないため
- ジェムのような数量も、専用のフィールドにせず `items` の1行として扱う([所持リソース](items.md))
- 新しい所持データを足すときも、種別を1つ増やしてこの形に合わせる
- 現状は所持データを手放す手段が無いため、`items`・`pachimon`・`pachimon_move_map` の `removed` は使われない
  (パーティの編成し直しでは `party_slots.removed` を使う)
- プレイヤー ID・ニックネームは差分の考え方に合わないため含めず、サインインのレスポンスのフィールドとして返す

## API ごとの中身

| API | `player_diff` の中身 |
|---|---|
| `PartyService/SetParty` | `party_slots`(以前の割当をすべて `removed`、新しい割当を `upserted`) |
| `PartyService/SetPachimonMove` | `items`(消費後の育成チケット)と `pachimon_move_map`(付け替えた1件) |
| `PartyService/SetPachimonStatPoints` | `pachimon`(割り振った後の1体)と、ポイントを増やしたときだけ `items`(消費後の育成チケット)。今と同じ値なら空 |
| `ScoutService/Roll` | `items`(消費後のジェム) |
| `ScoutService/Select` | `pachimon`(入手した1体)と `pachimon_move_map`(その技) |
| `ShopService/Purchase` | `items`(支払った後のジェムと、受け取った後のアイテム) |
| `DebugService/GrantGems` | `items`(付与後のジェム) |
| `DebugService/RandomizeParty` | `pachimon`・`pachimon_move_map`(新しく付与した6体)と `party_slots` |

## サインインの全件(snapshot)

サインインは所持データの全件を `player_diff` ではなく `snapshot`(`PlayerSnapshot`)で返す。差分としてではなく
手元のデータを置き換える全件で、`player_diff` と同じ名前にすると基盤が差分として自動で反映してしまうため、名前を分けた。

## サーバーでの集め方

`service/player_diff.rs` の `PlayerDiffCollector` が、1リクエストの間に変わった所持データを集める。書き込み関数が DB に書く
ついでに変更後の値を記録し、同じ行が何回変わっても最後の値だけを持つ。ハンドラーが処理の最後にレスポンスの `player_diff` に詰める。
トランザクションがロールバックされたらレスポンスごと捨てられるので、中身も残らない。

クライアントでの反映は [サービスと所持データ](../client/services-and-player-data.md)。
