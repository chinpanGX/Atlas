using System.Threading;
using Cysharp.Threading.Tasks;
using Supplement.Core;

namespace Atlas.Infrastructure
{
    // サインインはマスタを使わないため、同じ優先度で並列に読み込む。
    internal sealed class MasterDataInitializationTask : IBootInitializationTask
    {
        private readonly MasterDatabaseLoader loader;

        public MasterDataInitializationTask(MasterDatabaseLoader loader)
        {
            this.loader = loader;
        }

        public InitializationPriority Priority => InitializationPriority.Normal;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            return loader.LoadAsync(ct);
        }
    }
}
