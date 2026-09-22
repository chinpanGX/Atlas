using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Mock
{
    public sealed class MockPlayerRepository : IPlayerRepository
    {
        public UniTask<PlayerData> GetAsync()
        {
            return UniTask.FromResult(new PlayerData("mock-player-id", "プレイヤー", 300));
        }

        public UniTask<PlayerData> CreateAsync(string nickname)
        {
            return UniTask.FromResult(new PlayerData("mock-player-id", nickname, 300));
        }
    }
}
