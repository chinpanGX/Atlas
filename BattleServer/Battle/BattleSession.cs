using Atlas.BattleCore;
using Atlas.BattleServer.Contracts;
using Atlas.BattleServer.Internal;
using MagicOnion.Server.Hubs;

namespace Atlas.BattleServer.Battle
{
    public enum BattlePhase
    {
        WaitingForJoin,
        Selecting,      // 実際に使う、両者の選出待ち
        InProgress,
        Finished
    }

    // BattleEngine.ProcessTurnへ渡す行動と、報告用に元のMoveIdを合わせて保持する。
    public readonly record struct PendingAction(PlayerAction Action, string? MoveId);

    public sealed class BattleParticipant(string playerId)
    {
        public string PlayerId { get; } = playerId;

        // 切断中(または猶予中)はnull。
        public Guid? ConnectionId { get; set; }

        // 選出(SubmitSelectionAsync)前はnull。インデックスはBattleSide.Partyと一致する。
        public string[]? SelectedIds { get; set; }
        public ParticipantLoadout[]? Loadouts { get; set; }

        // 相手から見て公開済み(一度でも場に出た)かどうか。一度trueにしたら戻さない。
        public bool[] Revealed { get; set; } = [];

        public PendingAction? Pending { get; set; }

        // ターンタイムアウトで行動しなかったターンの連続回数(行動すれば0に戻る)。
        public int ConsecutiveIdleTurns { get; set; }
    }

    // 1対戦分のサーバー側の状態(docs/design/battle.mdでBattleStateと呼んでいるもの)。
    // Atlas.BattleCore.BattleStateとの名前衝突を避けるためBattleSessionとし、盤面はCoreに持つ。
    // 全フィールドはGateを取得した状態でのみ読み書きする。
    public sealed class BattleSession(string matchId)
    {
        public string MatchId { get; } = matchId;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public BattlePhase Phase { get; set; } = BattlePhase.WaitingForJoin;

        // インデックス0がBattleSideId.Player1、1がPlayer2(先にJoinAsyncした方がPlayer1)。
        public BattleParticipant?[] Participants { get; } = new BattleParticipant?[2];

        // グループが空になると作り直される可能性があるため、JoinAsyncのたびに最新の参照へ更新する。
        public IGroup<IBattleHubReceiver>? Group { get; set; }

        // InProgressになった時点で生成される。
        public BattleState? Core { get; set; }

        public CancellationTokenSource? SelectionTimer { get; set; }
        public CancellationTokenSource? TurnTimer { get; set; }

        // 切断(または未参加)の猶予タイマー。未参加の枠にも張るため、BattleParticipantではなく枠ごとに持つ。
        public CancellationTokenSource?[] AbsenceTimers { get; } = new CancellationTokenSource?[2];
        public List<BattleTurnRecord> TurnLog { get; } = [];

        public bool AllConnected => Participants.All(p => p?.ConnectionId is not null);

        public int IndexOf(string playerId) =>
            Array.FindIndex(Participants, p => p?.PlayerId == playerId);
    }
}
