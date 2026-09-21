using System.Threading;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    /// <summary>
    /// design/client-architecture.md「実装」参照。インメモリの擬似データを返すだけの実装。
    /// </summary>
    public sealed class MockPlayerConnection : IPlayerConnection
    {
        public async UniTask<PlayerData> GetMeAsync(CancellationToken token)
        {
            await UniTask.Delay(200, cancellationToken: token);
            return new PlayerData("mock-player-id", "プレイヤー", 300);
        }
    }
}
