-- players.gemsの初期値をShared/docs/design/outgame.mdの設計(DEFAULT 300)に合わせる。
-- スカウトの紹介コスト(150/回)の2回分に相当し、初回起動時点で最低限スカウトを
-- 試せるようにするための値(Shared/docs/design/battle.mdの「報酬設計(gems)」参照)。
ALTER TABLE players
    ALTER COLUMN gems SET DEFAULT 300;
