using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Mock
{
    public sealed class MockPlayerRepository : IPlayerRepository
    {
        public UniTask<PlayerData> GetMeAsync()
        {
            return UniTask.FromResult(new PlayerData("mock-player-id", "プレイヤー", 300));
        }
    }
}
