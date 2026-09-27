#nullable enable
using Atlas.BattleCore;
using MessagePack;

// IBattleHub/IBattleHubReceiverでやり取りするPayload(MessagePackでシリアライズする)。
// EffectivenessResult/BattleEndReasonはAtlas.BattleCoreの同名enum(定義も同一)をそのまま使う。
namespace Atlas.BattleContracts
{
    public enum JoinResultStatus { Success, InvalidToken, MatchNotFound, AlreadyJoined }

    [MessagePackObject]
    public sealed record JoinResult([property: Key(0)] JoinResultStatus Status);

    // MoveIdはmovesマスタのmove_id(int)を文字列化したもの。
    [MessagePackObject]
    public sealed record MoveRequest([property: Key(0)] string MoveId);

    // 選出画面の表示内容。SelfPartyは自分のパーティ(枠番号順)で、SubmitSelectionAsyncにはここから選んだ
    // PlayerPachimonIdを送る。相手のパーティはどのパチモンかだけ見せる(個体ID・技・努力値は送らない)。
    // RemainingSecondsは選出の残り時間(UIのタイマー表示用。再接続時の再送では経過分を引いた値になる)。
    // SelectionSubmittedは自分が選出を送信済みか(再接続時の再送用。送信済みなら相手の選出を待つだけ)。
    [MessagePackObject]
    public sealed record SelectionStartPayload(
        [property: Key(0)] PartyPachimon[] SelfParty,
        [property: Key(1)] int[] OpponentPartyPachimonIds,
        [property: Key(2)] int MaxSelectionCount,
        [property: Key(3)] int RemainingSeconds,
        [property: Key(4)] bool SelectionSubmitted);

    [MessagePackObject]
    public sealed record PartyPachimon(
        [property: Key(0)] string PlayerPachimonId,
        [property: Key(1)] int PachimonId);

    // TurnTimeLimitSecondsはUIのターンタイマー表示用(サーバー側の実際のタイムアウト判定とは別)。
    // SelfMovesは自分側の選出各枠の技(Self.SelectedPachimonとインデックスが対応)。クライアントは手元の
    // 所持データではなくこれを表示・送信することで、サーバーが判定に使う技と常に一致させる。
    // 相手側の技は非公開のため送らない。
    [MessagePackObject]
    public sealed record BattleStartPayload(
        [property: Key(0)] ParticipantSnapshot Self,
        [property: Key(1)] ParticipantSnapshot Opponent,
        [property: Key(2)] int TurnTimeLimitSeconds,
        [property: Key(3)] PachimonMoveSet[] SelfMoves);

    // 選出1枠分の技。Movesのインデックスが技スロット(PlayerAction.UseMoveのmoveIndex)。
    [MessagePackObject]
    public sealed record PachimonMoveSet([property: Key(0)] MoveState[] Moves);

    // CurrentPpはOnMatchStart送信時点の残りPP(再接続時の再送では消費済みの値になる)。
    [MessagePackObject]
    public sealed record MoveState(
        [property: Key(0)] string MoveId,
        [property: Key(1)] int CurrentPp,
        [property: Key(2)] int MaxPp);

    [MessagePackObject]
    public sealed record ParticipantSnapshot(
        [property: Key(0)] string PlayerId,
        [property: Key(1)] PachimonSlot[] SelectedPachimon,
        [property: Key(2)] int ActivePachimonIndex);

    // Selfは常に全枠IsRevealed=true。Opponentは場に出た枠だけIsRevealed=trueで、未公開の枠はState=null。
    [MessagePackObject]
    public sealed record PachimonSlot(
        [property: Key(0)] bool IsRevealed,
        [property: Key(1)] PachimonBattleState? State);

    // HpPercentは0〜100の整数。ダメージ計算式の逆算を防ぐため、HPの生値は送らない。
    [MessagePackObject]
    public sealed record PachimonBattleState(
        [property: Key(0)] string PlayerPachimonId,
        [property: Key(1)] int PachimonId,
        [property: Key(2)] int HpPercent,
        [property: Key(3)] bool IsFainted);

    [MessagePackObject]
    public sealed record TurnResultPayload(
        [property: Key(0)] int TurnNumber,
        [property: Key(1)] ActionResult[] Actions,
        [property: Key(2)] string[] PlayersRequiringForcedSwitch);

    // DamageDealtは送らない(HpPercentと同じ理由)。RevealedPachimonは枠が初めて場に出た時のみ入る。
    [MessagePackObject]
    public sealed record ActionResult(
        [property: Key(0)] string PlayerId,
        [property: Key(1)] ActionType Type,
        [property: Key(2)] string? MoveId,
        [property: Key(3)] bool Hit,
        [property: Key(4)] bool Critical,
        [property: Key(5)] EffectivenessResult Effectiveness,
        [property: Key(6)] int TargetRemainingHpPercent,
        [property: Key(7)] bool TargetFainted,
        [property: Key(8)] int? NewActiveIndex,
        [property: Key(9)] PachimonBattleState? RevealedPachimon);

    public enum ActionType { Move, Switch, Skip }   // Skip = タイムアウト or 強制交代未対応

    [MessagePackObject]
    public sealed record BattleEndPayload(
        [property: Key(0)] string WinnerId,
        [property: Key(1)] BattleEndReason Reason);
}
