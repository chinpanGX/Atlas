-- battle_matches.statusに'aborted'(勝者なしで終了)を追加する(Shared/docs/design/battle.md「DB設計」参照)。
--
-- 'aborted'になるのは次の2通り。どちらもwinner_idはNULLのまま、gemsも付与しない。
-- - BattleServerが勝者なし(winnerIdが空文字)で結果を報告した(両者未選出・両者放置等)
-- - 結果報告が無いまま一定時間経過した対戦を、APIサーバーの定期処理が打ち切った
--   (BattleServerの再起動、両者とも接続しなかった、結果報告の失敗等)
ALTER TABLE battle_matches
    MODIFY status ENUM('matching', 'in_progress', 'finished', 'aborted') NOT NULL;
