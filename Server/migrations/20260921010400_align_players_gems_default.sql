-- players.gemsの初期値を300にする。
-- スカウトの紹介コスト(150/回)の2回分に相当し、初回起動時点で最低限スカウトを
-- 試せるようにするための値。
ALTER TABLE players
    ALTER COLUMN gems SET DEFAULT 300;
