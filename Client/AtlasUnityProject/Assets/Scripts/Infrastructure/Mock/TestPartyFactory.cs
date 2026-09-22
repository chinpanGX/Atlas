using System.Collections.Generic;
using System.Linq;
using Atlas.BattleCore;
using Atlas.MasterData;
using MasterMemory;

namespace Atlas.Infrastructure.Mock
{
    // 選出3体1組の構成要素。PachimonStateだけではAtlas.BattleCore.MoveDataから元のmove_idが
    // 分からない(MoveDataにIDフィールドが無いため)、IBattleConnection実装が技選択のID変換に
    // 使うMoveIds(State.Movesと同じ並び)を一緒に保持する。
    public sealed record PartyMember(int PachimonId, PachimonState State, IReadOnlyList<string> MoveIds);

    // マスターデータ(pachimon/moves/move_group_moves)からAtlas.BattleCoreの選出3体を組み立てる。
    // player_pachimon(プレイヤー所持データ、個体値等)がまだ存在しないため、種族のbase値を
    // そのまま使い、IV/EVは0として扱う。テスト・Mock対戦用の暫定実装(design/battle.md
    // 「Stage 1」参照)。
    public static class TestPartyFactory
    {
        // design/battle.md「実効ステータス計算」: 経験値によるレベルアップを持たないため
        // 全パチモン固定レベル50。
        private const int FixedLevel = 50;

        public static IReadOnlyList<PartyMember> BuildParty(MemoryDatabase database, IReadOnlyList<int> pachimonIds)
        {
            var members = new List<PartyMember>(pachimonIds.Count);
            foreach (var pachimonId in pachimonIds)
            {
                var pachimon = database.PachimonDataTable.FindByPachimonId(pachimonId);
                var moves = PachimonMoveLookup.GetInitialMoves(database, pachimonId);

                var stats = new ParticipantStats(
                    Level: FixedLevel,
                    Hp: CalculateStat(pachimon.BaseHp, isHp: true),
                    Atk: CalculateStat(pachimon.BaseAtk, isHp: false),
                    Def: CalculateStat(pachimon.BaseDef, isHp: false),
                    SpAtk: CalculateStat(pachimon.BaseSpatk, isHp: false),
                    SpDef: CalculateStat(pachimon.BaseSpdef, isHp: false),
                    Speed: CalculateStat(pachimon.BaseSpeed, isHp: false),
                    PrimaryType: MasterDataConversions.ToElementType(pachimon.PrimaryType),
                    SecondaryType: MasterDataConversions.ToSecondaryElementType(pachimon.SecondaryType));

                var moveDataList = moves
                    .Select(m => new MoveData(
                        MasterDataConversions.ToElementType(m.MoveType),
                        MasterDataConversions.ToMoveCategory(m.Category),
                        m.BasePower,
                        m.Accuracy,
                        m.MaxPp))
                    .ToList();

                var moveIds = moves.Select(m => m.MoveId.ToString()).ToList();

                members.Add(new PartyMember(pachimonId, new PachimonState(stats, moveDataList), moveIds));
            }

            return members;
        }

        // floor((2*base + IV + floor(EV/4)) * level / 100) + 5 (HPのみ + level + 10)。
        // player_pachimonが未実装のためIV/EVは常に0として扱う。
        private static int CalculateStat(int baseStat, bool isHp)
        {
            var value = 2 * baseStat * FixedLevel / 100;
            return isHp ? value + FixedLevel + 10 : value + 5;
        }
    }
}
