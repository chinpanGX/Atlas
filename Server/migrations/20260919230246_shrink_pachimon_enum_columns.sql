-- pachimon.primary_type/secondary_type/rarityは、実際の値域(PachimonType: 0-18,
-- Rarity: 1-4)に対してBIGINTが過大だったため、TINYINT UNSIGNED(0-255)へ変更する。
-- 起動時に1回DBから読み込みメモリキャッシュとして保持する設計のため、パフォーマンス上の
-- 意味合いは小さく、値域に合わせたスキーマの正しさ・自己文書化が主な目的。
ALTER TABLE pachimon
    MODIFY COLUMN primary_type TINYINT UNSIGNED NOT NULL,
    MODIFY COLUMN secondary_type TINYINT UNSIGNED NOT NULL,
    MODIFY COLUMN rarity TINYINT UNSIGNED NOT NULL;
