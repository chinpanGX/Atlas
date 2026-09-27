using Atlas.BattleContracts;
using Atlas.BattleCore;

namespace Atlas.BattleServer.Battle
{
    // 選出された1体を対戦用に組み立てるための情報。Movesのインデックスがそのまま
    // PlayerAction.UseMoveのmoveIndexになる。
    public sealed record ParticipantLoadout(
        int PachimonId,
        ParticipantStats Stats,
        IReadOnlyList<LoadoutMove> Moves);

    public sealed record LoadoutMove(string MoveId, MoveData Data);

    // 選出候補(パーティ編成)の取得、player_pachimon_id→BattleCore用の型(ParticipantStats/MoveData)への変換と、
    // タイプ相性の提供。
    // 本実装はApiParticipantDataSource(Rustの内部APIで所持データを取得し、マスタと組み合わせる)。
    public interface IParticipantDataSource
    {
        // playerIdの現在のパーティ編成(選出候補)を枠番号順に返す。
        Task<IReadOnlyList<PartyPachimon>> GetPartyAsync(string playerId);

        // 選出分をまとめて解決し、playerPachimonIdsと同じ順番で返す。playerIdの所持でない個体
        // (存在しないIDを含む)が1体でもあればnullを返す。
        Task<IReadOnlyList<ParticipantLoadout>?> ResolveAsync(string playerId, IReadOnlyList<string> playerPachimonIds);

        ITypeChart TypeChart { get; }
    }
}
