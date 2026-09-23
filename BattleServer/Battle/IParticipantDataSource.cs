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

    // player_pachimon_id→BattleCore用の型(ParticipantStats/MoveData)への変換と、タイプ相性の提供。
    // 本実装はApiParticipantDataSource(Rustの内部APIで所持データを取得し、マスタと組み合わせる)。
    public interface IParticipantDataSource
    {
        // 選出分をまとめて解決し、playerPachimonIdsと同じ順番で返す。playerIdの所持でない個体
        // (存在しないIDを含む)が1体でもあればnullを返す。
        Task<IReadOnlyList<ParticipantLoadout>?> ResolveAsync(string playerId, IReadOnlyList<string> playerPachimonIds);

        ITypeChart TypeChart { get; }
    }
}
