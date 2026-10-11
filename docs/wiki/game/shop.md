---
title: "ショップ(追加予定)"
status: planned
area: game
related: [game/party-training, master-data/tables, conventions/items, open-questions]
code: [Shared/master-data/csv/shop_products_master.csv]
updated: 2026-10-11
---

# ショップ(追加予定)

ジェムを使ってアイテムを購入する。今のところ商品は育成チケットだけ。

- 商品を選び、表示された値段のジェムを払って購入する。ジェムが足りなければ購入できない
- 購入したアイテムは所持品として持ち、使うと減る(育成チケットは技の付け替えか割り振りの変更で1枚使う)
- 育成チケットは1枚50ジェム(仮の値)
- 商品はマスタで持ち、販売期間は無い(マスタにある商品はいつでも買える)
- 購入履歴は残さない
- 所持しているアイテムの数を確認できる

## いま実装にあるもの

- マスタ `items` の育成チケット(`item_id: 2`)と、マスタ `shop_products` の商品1件(育成チケット1枚 = ジェム50)
- 購入の API・DB のテーブル・画面は無い

未確定のこと(まとめ買いの可否、購入数の上限)は [未確定・保留](../open-questions.md)。
