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
    }

    public interface IBattleHubReceiver
    {
        void OnSelectionStart(SelectionStartPayload payload);   // 両者の参加が揃ってから発火。選出中の再接続時は再接続者にだけ再送する
        void OnMatchStart(BattleStartPayload payload);   // 選出が揃ってから発火。再接続時は再接続者にだけ再送する
        void OnTurnResult(TurnResultPayload payload);
        void OnBattleEnd(BattleEndPayload payload);

        // IBattleConnectionのOnOpponentDisconnected/OnOpponentReconnectedに対応する
        // (切断から60秒の猶予中は、両者とも行動できない)。
        void OnOpponentDisconnected();
        void OnOpponentReconnected();
    }
}
