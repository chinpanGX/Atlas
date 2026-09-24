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
    // player_pachimon(プレイヤー所持データ)がまだ存在しないため、種族のbase値を
    // そのまま使い、努力値(EV)は0として扱う。テスト・Mock対戦用の暫定実装(design/battle.md
    // 「Stage 1」参照)。
    public static class TestPartyFactory
    {
        public static IReadOnlyList<PartyMember> BuildParty(MemoryDatabase database, IReadOnlyList<int> pachimonIds)
        {
            var members = new List<PartyMember>(pachimonIds.Count);
            foreach (var pachimonId in pachimonIds)
            {
                var pachimon = database.PachimonDataTable.FindByPachimonId(pachimonId);
                var moves = PachimonMoveLookup.GetInitialMoves(database, pachimonId);

                var stats = new ParticipantStats(
                    Level: PachimonStatCalculator.FixedLevel,
                    Hp: PachimonStatCalculator.CalculateHp(pachimon.BaseHp),
                    Atk: PachimonStatCalculator.CalculateOther(pachimon.BaseAtk),
                    Def: PachimonStatCalculator.CalculateOther(pachimon.BaseDef),
                    SpAtk: PachimonStatCalculator.CalculateOther(pachimon.BaseSpatk),
                    SpDef: PachimonStatCalculator.CalculateOther(pachimon.BaseSpdef),
                    Speed: PachimonStatCalculator.CalculateOther(pachimon.BaseSpeed),
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
    }
}
