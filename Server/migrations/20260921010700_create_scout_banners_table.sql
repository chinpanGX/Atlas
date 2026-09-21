-- スカウトバナーマスタ(Shared/docs/design/scout.md参照)。
--
-- master-data-pipelineの対象外とし、専用のseedバイナリ(seed_scout_banners)で
-- Rust側から直接INSERT/UPSERTする運用とする。理由は2つ:
-- 開催期間(start_at/end_at)を持つ運用寄りのデータであり、クライアント再ビルド無しで
-- 切り替えられるようにしたいこと、そして排出率(rate_table)をクライアントに配布しないため。
CREATE TABLE scout_banners (
    banner_id CHAR(26) PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    rate_table JSON NOT NULL,
    cost_per_roll INT NOT NULL,
    start_at DATETIME(3) NOT NULL,
    end_at DATETIME(3) NOT NULL
);
