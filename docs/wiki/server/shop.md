---
title: "ショップ"
status: current
area: server
related: [game/shop, conventions/items, conventions/player-diff, master-data/tables, server/game-log]
proto: [Shared/proto/atlas/v1/shop.proto]
code: [Server/src/grpc/shop.rs, Server/src/service/shop_service.rs, Server/src/master/mod.rs, Server/src/bin/seed_master_data.rs]
updated: 2026-10-17
---

# ショップ

商品はマスタ `shop_products`([マスタのテーブル](../master-data/tables.md))で持ち、起動時に読み込んだ `MasterData` から引く。
販売期間・購入履歴は無い。

## ShopService/ListProducts

→ `products`(`shop_product_id`・`item_id`・`amount`・`cost_item_id`・`cost_amount`)

- マスタの商品を `shop_product_id` の順にすべて返す。値段はクライアントに見せてよい値なので、マスタの列をそのまま返す
- 読むだけなので `NO_SIDE_EFFECTS`

## ShopService/Purchase

`shop_product_id` → `player_diff`

- 商品を1つ購入する。`cost_item_id` を `cost_amount` 個払い、`item_id` を `amount` 個受け取る。1回の購入で買うのは1つだけ
  (一度に複数個買えるかは[未確定](../open-questions.md))
- 検証(どれかに当たれば何も変えない)
  1. 商品が無い → `SHOP_PRODUCT_NOT_FOUND`
  2. ジェムが足りない → `INSUFFICIENT_GEMS`(スカウトと同じ reason)
- 払うのは `player_item_service::deduct`(`quantity >= 値段` の行だけを更新するので、不足を1つのクエリで判定できる)、受け取るのは
  `player_item_service::add`(行が無ければ作る)。同じトランザクションで行う
- `player_diff` の `items` に、払った後のジェムと受け取った後のアイテムの両方が入る
- ゲームログには、払った分と受け取った分の2件を、どちらも理由 `shop_purchase` で残す
- 冪等性の対象。同じ RequestID の再送では二重に購入しない

## 商品のマスタの検証

`seed_master_data` が投入する前に、パイプラインの `validate` で確かめられない決まりを確かめ、合わなければ何も投入せずに止める。

- `amount`・`cost_amount` が1以上
- `cost_item_id` がジェム(`item_id: 1`)。購入の失敗をジェム不足(`INSUFFICIENT_GEMS`)で返すため。ジェム以外で払う商品を足すときは、
  足りないときの業務エラーと合わせて決め直す

商品を参照するプレイヤーのデータは無いので、投入は全件を消してから入れ直す(マスタから消した商品が残らないように)。
