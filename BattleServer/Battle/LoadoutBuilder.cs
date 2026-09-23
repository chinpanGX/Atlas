using Atlas.BattleCore;
using Atlas.MasterData;
using Atlas.MasterData.Enums;
using BattleMoveCategory = Atlas.BattleCore.MoveCategory;
using MasterMoveCategory = Atlas.MasterData.Enums.MoveCategory;

namespace Atlas.BattleServer.Battle
{
    // 所持データ1体分(Rustの/internal/battle/loadoutsが返すもの)。種族値・技の性能はマスタから引く。
    public sealed record OwnedPachimon(int PachimonId, EffortValues EffortValues, IReadOnlyList<int> MoveIds);

    public sealed record EffortValues(int Hp, int Atk, int Def, int SpAtk, int SpDef, int Speed);

    // マスタ(MemoryDatabase)と所持データから、BattleCore用の型(ParticipantLoadout/ITypeChart)を組み立てる。
    // Client側のMock(Atlas.Infrastructure.Mock.TestPartyFactory/MasterDataTypeChart)と同じ変換。
    public static class LoadoutBuilder
    {
        // design/battle.md「実効ステータス計算」のlevel(固定値)。
        public const int FixedLevel = 50;

        public static ParticipantLoadout Build(MemoryDatabase database, OwnedPachimon owned)
        {
            if (!database.PachimonDataTable.TryFindByPachimonId(owned.PachimonId, out var pachimon))
            {
                throw new InvalidOperationException($"pachimon_id {owned.PachimonId} is not in master data");
            }

            var ev = owned.EffortValues;
            var stats = new ParticipantStats(
                Level: FixedLevel,
                Hp: CalculateHp(pachimon.BaseHp, ev.Hp),
                Atk: CalculateOther(pachimon.BaseAtk, ev.Atk),
                Def: CalculateOther(pachimon.BaseDef, ev.Def),
                SpAtk: CalculateOther(pachimon.BaseSpatk, ev.SpAtk),
                SpDef: CalculateOther(pachimon.BaseSpdef, ev.SpDef),
                Speed: CalculateOther(pachimon.BaseSpeed, ev.Speed),
                PrimaryType: ToElementType(pachimon.PrimaryType),
                SecondaryType: ToSecondaryElementType(pachimon.SecondaryType));

            var moves = owned.MoveIds
                .Select(moveId =>
                {
                    if (!database.MovesDataTable.TryFindByMoveId(moveId, out var move))
                    {
                        throw new InvalidOperationException($"move_id {moveId} is not in master data");
                    }

                    return new LoadoutMove(
                        moveId.ToString(),
                        new MoveData(ToElementType(move.MoveType), ToMoveCategory(move.Category), move.BasePower, move.Accuracy, move.MaxPp));
                })
                .ToList();

            return new ParticipantLoadout(owned.PachimonId, stats, moves);
        }

        public static ITypeChart BuildTypeChart(MemoryDatabase database) => new MasterDataTypeChart(database);

        // HP = floor((2 * base + floor(EV/4)) * level / 100) + level + 10
        public static int CalculateHp(int baseStat, int effortValue) =>
            (2 * baseStat + effortValue / 4) * FixedLevel / 100 + FixedLevel + 10;

        // HP以外 = floor((2 * base + floor(EV/4)) * level / 100) + 5
        public static int CalculateOther(int baseStat, int effortValue) =>
            (2 * baseStat + effortValue / 4) * FixedLevel / 100 + 5;

        public static ElementType ToElementType(PachimonType type) => type switch
        {
            PachimonType.Normal => ElementType.Normal,
            PachimonType.Fire => ElementType.Fire,
            PachimonType.Water => ElementType.Water,
            PachimonType.Electric => ElementType.Electric,
            PachimonType.Grass => ElementType.Grass,
            PachimonType.Ice => ElementType.Ice,
            PachimonType.Fighting => ElementType.Fighting,
            PachimonType.Poison => ElementType.Poison,
            PachimonType.Ground => ElementType.Ground,
            PachimonType.Flying => ElementType.Flying,
            PachimonType.Psychic => ElementType.Psychic,
            PachimonType.Bug => ElementType.Bug,
            PachimonType.Rock => ElementType.Rock,
            PachimonType.Ghost => ElementType.Ghost,
            PachimonType.Dragon => ElementType.Dragon,
            PachimonType.Dark => ElementType.Dark,
            PachimonType.Steel => ElementType.Steel,
            PachimonType.Fairy => ElementType.Fairy,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type,
                "PachimonType.Noneはprimary_type/move_typeには使えません。"),
        };

        // secondary_typeのNone(=セカンドタイプ無し)はnullで表現する。
        public static ElementType? ToSecondaryElementType(PachimonType type) =>
            type == PachimonType.None ? null : ToElementType(type);

        public static BattleMoveCategory ToMoveCategory(MasterMoveCategory category) => category switch
        {
            MasterMoveCategory.Physical => BattleMoveCategory.Physical,
            MasterMoveCategory.Special => BattleMoveCategory.Special,
            MasterMoveCategory.Status => BattleMoveCategory.Status,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        public static EffectivenessResult ToEffectivenessResult(TypeEffectiveness effectiveness) => effectiveness switch
        {
            TypeEffectiveness.Immune => EffectivenessResult.Immune,
            TypeEffectiveness.NotVeryEffective => EffectivenessResult.NotVeryEffective,
            TypeEffectiveness.Normal => EffectivenessResult.Normal,
            TypeEffectiveness.SuperEffective => EffectivenessResult.SuperEffective,
            _ => throw new ArgumentOutOfRangeException(nameof(effectiveness), effectiveness, null),
        };

        // type_chartマスタは単体タイプ同士の組み合わせのみ持つ(複合タイプの掛け合わせはBattleCoreの
        // DamageCalculatorが2回引いて計算する、design/architecture.md参照)。
        private sealed class MasterDataTypeChart : ITypeChart
        {
            private readonly Dictionary<(ElementType Attack, ElementType Defend), EffectivenessResult> _table = new();

            public MasterDataTypeChart(MemoryDatabase database)
            {
                foreach (var row in database.TypeChartDataTable.All)
                {
                    _table[(ToElementType(row.AttackType), ToElementType(row.DefendType))] = ToEffectivenessResult(row.Effectiveness);
                }
            }

            public EffectivenessResult GetEffectiveness(ElementType attackType, ElementType defendType) =>
                _table[(attackType, defendType)];
        }
    }
}
