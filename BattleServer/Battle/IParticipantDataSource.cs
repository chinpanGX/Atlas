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
    // 本実装はDomain.MasterData(master-data-pipelineのcopy-models)が配置された後に行う。
    public interface IParticipantDataSource
    {
        // 解決できないplayer_pachimon_idの場合はnullを返す。
        ParticipantLoadout? Resolve(string playerId, string playerPachimonId);

        ITypeChart TypeChart { get; }
    }
}
