using System.Collections.Generic;
using System.Linq;
using Atlas.MasterData.Models;
using MasterMemory;

namespace Atlas.MasterData
{
    // pachimon.move_group_idからmove_group_moves→movesを辿って技を取得する共通クエリ。
    public static class PachimonMoveLookup
    {
        // 技の付け替えの候補。技グループのすべての技で、最初の技に戻せるよう初期習得技も含む。
        public static IReadOnlyList<MovesData> GetLearnableMoves(MemoryDatabase database, int pachimonId)
        {
            var pachimon = database.PachimonDataTable.FindByPachimonId(pachimonId);
            return database.MoveGroupMovesDataTable.All
                .Where(m => m.GroupId == pachimon.MoveGroupId)
                .Select(m => database.MovesDataTable.FindByMoveId(m.MoveId))
                .ToList();
        }
    }
}
