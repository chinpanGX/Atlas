-- スカウトのバナーをマスターデータ(seed_master_data で投入する)にする。
-- パイプラインに JSON の型が無いので、排出率を rate_table(JSON)から、レア度ごとの1行に分ける。
-- 日時の列は DATETIME(3) のまま(マスタの CSV では UTC の文字列で持ち、投入するときに変換する)。
ALTER TABLE scout_banners DROP COLUMN rate_table;

-- weight は1万分率の整数(3% = 300)。バナーごとの合計が10000であることは seed_master_data が確かめる。
-- パイプラインは複合主キーに対応していないので、(banner_id, rarity) の代わりに代理キーを持つ
CREATE TABLE scout_banner_rates (
    scout_banner_rate_id INT PRIMARY KEY,
    banner_id CHAR(26) NOT NULL,
    rarity TINYINT UNSIGNED NOT NULL,
    weight INT NOT NULL,
    FOREIGN KEY (banner_id) REFERENCES scout_banners(banner_id)
);
