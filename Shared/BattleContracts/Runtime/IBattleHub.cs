using System.Threading.Tasks;
using MagicOnion;

namespace Atlas.BattleContracts
{
    // BattleServerとUnity Clientの通信契約。Client側のIBattleConnectionは
    // このHub/Receiverと対になるメソッド構成を持つ。
    // Shared/BattleContracts(Unityのローカルパッケージ)に置き、BattleServer(BattleServer/BattleContracts/
    // Atlas.BattleContracts.csproj経由)とUnity Client(RealtimeBattleConnection)の両方から参照する。
    public interface IBattleHub : IStreamingHub<IBattleHub, IBattleHubReceiver>
    {
        Task<JoinResult> JoinAsync(string battleToken, string matchId);
        Task SubmitSelectionAsync(string[] playerPachimonIds);  // OnSelectionStartのSelfPartyから1〜MaxSelectionCount体
        Task SubmitMoveAsync(MoveRequest move);
        Task SwitchAsync(int partySlot);
        Task ForfeitAsync();

        // ターン結果の演出を流し終え、行動を入力できる状態になったことを知らせる(対戦開始・再接続の後も送る)。
        // サーバーは両者がそろってからターンの制限時間を数え始める(演出の長さで選べる時間が減らないようにするため)。
        Task ReadyForTurnAsync();
    }

    public interface IBattleHubReceiver
    {
        void OnSelectionStart(SelectionStartPayload payload);   // 両者の参加が揃ってから発火。選出中の再接続時は再接続者にだけ再送する
        void OnMatchStart(BattleStartPayload payload);   // 選出が揃ってから発火。再接続時は再接続者にだけ再送する
        void OnTurnResult(TurnResultPayload payload);
        void OnBattleEnd(BattleEndPayload payload);

        // ターンの制限時間を数え始めた(両者の準備がそろった、または準備待ちの上限を過ぎた)。秒数は制限時間そのもの。
        void OnTurnStart(int timeLimitSeconds);

        // IBattleConnectionのOnOpponentDisconnected/OnOpponentReconnectedに対応する
        // (切断から60秒の猶予中は、両者とも行動できない)。
        void OnOpponentDisconnected();
        void OnOpponentReconnected();
    }
}
