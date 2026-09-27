using Atlas.BattleContracts;
using Atlas.BattleCore;
using Atlas.BattleServer.Battle;

namespace Atlas.BattleServer.Tests
{
    // BattleHubTests用のIParticipantDataSource。Hubの進行(参加・選出・ターン・切断)だけを検証するため、
    // player_pachimon_idに関わらず固定ステータス・固定技で組み立て、所持チェックもしない。
    // パーティはPlayerIdの末尾の文字(小文字)+枠番号の6体("pA"なら"a1"〜"a6")で、PachimonIdは1001〜1006。
    // 本物(ApiParticipantDataSource)の変換はLoadoutBuilderTests/ApiParticipantDataSourceTestsで検証する。
    public sealed class DummyParticipantDataSource : IParticipantDataSource
    {
        // 種族値オール80・努力値0・Lv50相当(HP以外=floor(2*80*50/100)+5、HP=floor(2*80*50/100)+50+10)。
        private static readonly ParticipantStats DummyStats = new(
            Level: 50, Hp: 140, Atk: 85, Def: 85, SpAtk: 85, SpDef: 85, Speed: 85,
            PrimaryType: ElementType.Normal, SecondaryType: null);

        // movesマスタの汎用技(19: ぶつかり, 20: エナジーウェーブ)と同じ値。
        private static readonly IReadOnlyList<LoadoutMove> DummyMoves =
        [
            new("19", new MoveData(ElementType.Normal, MoveCategory.Physical, BasePower: 40, Accuracy: 100, MaxPp: 35)),
            new("20", new MoveData(ElementType.Normal, MoveCategory.Special, BasePower: 40, Accuracy: 100, MaxPp: 35)),
        ];

        private const int DummyPachimonId = 1001;

        public const int PartySize = 6;

        public ITypeChart TypeChart { get; } = new AllNormalTypeChart();

        public Task<IReadOnlyList<PartyPachimon>> GetPartyAsync(string playerId) =>
            Task.FromResult<IReadOnlyList<PartyPachimon>>(
                Enumerable.Range(1, PartySize)
                    .Select(i => new PartyPachimon($"{char.ToLowerInvariant(playerId[^1])}{i}", DummyPachimonId + i - 1))
                    .ToList());

        public Task<IReadOnlyList<ParticipantLoadout>?> ResolveAsync(string playerId, IReadOnlyList<string> playerPachimonIds) =>
            Task.FromResult<IReadOnlyList<ParticipantLoadout>?>(
                playerPachimonIds.Select(_ => new ParticipantLoadout(DummyPachimonId, DummyStats, DummyMoves)).ToList());

        private sealed class AllNormalTypeChart : ITypeChart
        {
            public EffectivenessResult GetEffectiveness(ElementType attackType, ElementType defendType) =>
                EffectivenessResult.Normal;
        }
    }
}
