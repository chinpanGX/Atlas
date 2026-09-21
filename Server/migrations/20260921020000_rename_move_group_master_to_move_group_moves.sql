-- move_group_masterテーブルをmove_group_movesへリネームする。
--
-- 「Master」という語がMasterMemoryのMemoryTable/MasterDataLoader等、別の意味で使われている
-- 命名(master-data-pipeline全体の呼称)と紛らわしいための改名。テーブルの中身(技グループが
-- 含む技の対応表)自体は変わらない(Shared/docs/design/architecture.md参照)。
ALTER TABLE move_group_master RENAME TO move_group_moves;
