using System.Threading;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Mock
{
    // オフライン用。待たずに成立したことにする。
    public sealed class MockBattleMatchmaker : IBattleMatchmaker
    {
        public UniTask<BattleMatch> FindMatchAsync(CancellationToken cancellation)
        {
            return UniTask.FromResult(new BattleMatch("mock-match", string.Empty, "mock-token"));
        }
    }
}
