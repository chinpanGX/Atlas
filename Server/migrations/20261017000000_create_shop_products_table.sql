-- ショップの商品マスタ。master-data-pipelineが生成するmaster_data/shop_products.jsonを
-- `cargo run --bin seed_master_data`でUPSERTする。
--
-- 1回の購入で item_id を amount 個受け取り、cost_item_id を cost_amount 個払う。
-- 購入履歴は持たないので、このテーブルを参照するプレイヤーのデータは無い。
CREATE TABLE shop_products (
    shop_product_id INT PRIMARY KEY,
    item_id INT NOT NULL,
    amount INT NOT NULL,
    cost_item_id INT NOT NULL,
    cost_amount INT NOT NULL,
    FOREIGN KEY (item_id) REFERENCES items(item_id),
    FOREIGN KEY (cost_item_id) REFERENCES items(item_id)
);
