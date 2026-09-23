using System;
using Cysharp.Threading.Tasks;

namespace Atlas.Domain
{
    // design/battle.md「IBattleConnection / Payload定義」参照。Client側の画面・進行制御は
    // このインターフェースのみに依存し、Atlas.BattleCoreやDomain.MasterDataを直接知らない。
    // MockBattleConnection/RealtimeBattleConnectionはこれを実装する。
    public interface IBattleConnection
    {
        UniTask<JoinResult> JoinAsync(string battleToken, string matchId);
        UniTask SubmitSelectionAsync(string[] playerPachimonIds);
        UniTask SubmitMoveAsync(MoveRequest move);
        UniTask SwitchAsync(int partySlot);
        UniTask ForfeitAsync();

        event Action<BattleStartPayload> OnMatchStart;
        event Action<TurnResultPayload> OnTurnResult;
        event Action<BattleEndPayload> OnBattleEnd;
        event Action OnOpponentDisconnected;
        event Action OnOpponentReconnected;
    }

    public enum JoinResultStatus { Success, InvalidToken, MatchNotFound, AlreadyJoined }

    public sealed record JoinResult(JoinResultStatus Status);

    public sealed record MoveRequest(string MoveId);

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
    // Opponentは初期状態でActivePachimonIndexの1体だけtrue(design/battle.md参照)。
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
