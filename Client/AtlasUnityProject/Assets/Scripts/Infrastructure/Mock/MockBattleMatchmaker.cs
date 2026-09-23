using System.Threading;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Mock
{
    // オフライン用。待たずに成立したことにする。MockBattleConnectionは選出IDをマスターデータの
    // PachimonIdとして解釈するため、所持パチモン(PlayerPachimonId)ではなく固定のPachimonIdを返す。
    public sealed class MockBattleMatchmaker : IBattleMatchmaker
    {
        private static readonly string[] SelectedPachimonIds = { "1001", "1002", "1003" };

        public UniTask<BattleMatch> FindMatchAsync(CancellationToken cancellation)
        {
            return UniTask.FromResult(new BattleMatch("mock-match", string.Empty, "mock-token", SelectedPachimonIds));
        }
    }
}
