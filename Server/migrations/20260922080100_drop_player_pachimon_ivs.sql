-- 個体値(IV)の概念を廃止する(ポケモンチャンピオンズ準拠)。
--
-- 実効ステータス計算からIV項を除去し、種族値と努力値(effort_values、現状は常に0固定)のみで
-- 決まる形にする。
ALTER TABLE player_pachimon DROP COLUMN ivs;
