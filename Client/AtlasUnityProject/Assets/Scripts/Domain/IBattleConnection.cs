using System;
using Cysharp.Threading.Tasks;

namespace Atlas.Domain
{
    // BattleServerとの対戦の接続。IBattleHub/IBattleHubReceiver(Shared/BattleContracts)と対になる。Client側の画面・進行制御は
    // このインターフェースのみに依存し、Atlas.BattleCoreやDomain.MasterDataを直接知らない。
    // MockBattleConnection/RealtimeBattleConnectionはこれを実装する。
    public interface IBattleConnection
    {
        UniTask<JoinResult> JoinAsync(string battleToken, string matchId);
        UniTask SubmitSelectionAsync(string[] playerPachimonIds);
        UniTask SubmitMoveAsync(MoveRequest move);
        UniTask SwitchAsync(int partySlot);
        UniTask ForfeitAsync();

        event Action<SelectionStartPayload> OnSelectionStart;
        event Action<BattleStartPayload> OnMatchStart;
        event Action<TurnResultPayload> OnTurnResult;
        event Action<BattleEndPayload> OnBattleEnd;
        event Action OnOpponentDisconnected;
        event Action OnOpponentReconnected;
    }

    public enum JoinResultStatus { Success, InvalidToken, MatchNotFound, AlreadyJoined }

    public sealed record JoinResult(JoinResultStatus Status);

    public sealed record MoveRequest(string MoveId);

    // 選出画面の表示内容。両者の参加が揃った時点で届く。SelfPartyは自分の
    // パーティ(枠番号順)で、SubmitSelectionAsyncにはここから1〜MaxSelectionCount体選んで送る。相手のパーティは
    // どのパチモンかだけ分かる(個体ID・技は送られない)。RemainingSecondsは選出の残り時間(選出中の再接続で
    // 再送された場合は経過分を引いた値)。SelectionSubmittedは自分が選出を送信済みか。
    public sealed record SelectionStartPayload(
        PartyPachimon[] SelfParty, int[] OpponentPartyPachimonIds, int MaxSelectionCount, int RemainingSeconds,
        bool SelectionSubmitted);

    public sealed record PartyPachimon(string PlayerPachimonId, int PachimonId);

    // SelfMovesは自分側の選出各枠の技(Self.SelectedPachimonとインデックスが対応)。サーバーが判定に使う
    // 技そのものなので、技の表示・送信は手元の所持データではなくこれを使う。
    public sealed record BattleStartPayload(
        ParticipantSnapshot Self, ParticipantSnapshot Opponent, int TurnTimeLimitSeconds, PachimonMoveSet[] SelfMoves);

    // 選出1枠分の技。Movesのインデックスが技スロット。
    public sealed record PachimonMoveSet(MoveState[] Moves);

    public sealed record MoveState(string MoveId, int CurrentPp, int MaxPp);

    public sealed record ParticipantSnapshot(
        string PlayerId, PachimonSlot[] SelectedPachimon, int ActivePachimonIndex);

    // 選出3体のうち「場に出た(=公開された)」枠だけがIsRevealed=true。Selfは常に全枠true、
    // Opponentは初期状態でActivePachimonIndexの1体だけtrue。一度公開された枠は控えに戻っても公開のまま。
    public sealed record PachimonSlot(bool IsRevealed, PachimonBattleState State);

    // Levelは持たない(全パチモン共通の固定値のためUIが固定表示すればよい)。HpPercentは0〜100の
    // 整数で、CurrentHp/MaxHpの生値は送らない(ダメージ計算式の定数を逆算されないようにするため)。
    public sealed record PachimonBattleState(
        string PlayerPachimonId, int PachimonId, int HpPercent, bool IsFainted);

    public sealed record TurnResultPayload(
        int TurnNumber, ActionResult[] Actions, string[] PlayersRequiringForcedSwitch);

    public sealed record ActionResult(
        string PlayerId, ActionType Type, string MoveId,
        bool Hit, bool Critical, EffectivenessResult Effectiveness,
        int TargetRemainingHpPercent, bool TargetFainted,
        int? NewActiveIndex, PachimonBattleState RevealedPachimon);

    public enum ActionType { Move, Switch, Skip }

    // type_chartマスタのeffectiveness(ENUM)とは別物。Atlas.BattleCore.EffectivenessResultとも
    // 別の型(Domain層はAtlas.BattleCoreを直接知らないため)。
    public enum EffectivenessResult { Immune, NotVeryEffective, Normal, SuperEffective }

    public sealed record BattleEndPayload(string WinnerId, BattleEndReason Reason);

    public enum BattleEndReason { AllFainted, Forfeit, DisconnectTimeout }
}
