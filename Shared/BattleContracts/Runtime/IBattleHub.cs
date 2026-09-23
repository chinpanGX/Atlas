using System.Threading.Tasks;
using MagicOnion;

namespace Atlas.BattleContracts
{
    // docs/design/battle.md「MagicOnion Hub設計(C#側)」の契約。Client側のIBattleConnectionは
    // このHub/Receiverと対になるメソッド構成を持つ。
    // Shared/BattleContracts(Unityのローカルパッケージ)に置き、BattleServer(BattleServer/BattleContracts/
    // Atlas.BattleContracts.csproj経由)とUnity Client(RealtimeBattleConnection)の両方から参照する。
    public interface IBattleHub : IStreamingHub<IBattleHub, IBattleHubReceiver>
    {
        Task<JoinResult> JoinAsync(string battleToken, string matchId);
        Task SubmitSelectionAsync(string[] playerPachimonIds);  // 常に3体固定、クライアントが自動送信
        Task SubmitMoveAsync(MoveRequest move);
        Task SwitchAsync(int partySlot);
        Task ForfeitAsync();
    }

    public interface IBattleHubReceiver
    {
        void OnMatchStart(BattleStartPayload payload);   // 選出が揃ってから発火。再接続時は再接続者にだけ再送する
        void OnTurnResult(TurnResultPayload payload);
        void OnBattleEnd(BattleEndPayload payload);

        // IBattleConnectionのOnOpponentDisconnected/OnOpponentReconnectedに対応する
        // (docs/design/battle.md「再接続時の盤面復元」)。
        void OnOpponentDisconnected();
        void OnOpponentReconnected();
    }
}
