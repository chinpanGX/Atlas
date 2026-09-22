using System.Threading;
using Atlas.Addressables;
using Atlas.Application;
using Atlas.MasterData;
using Cysharp.Threading.Tasks;
using MasterMemory;
using Supplement.Loader.Abstractions;
using UnityEngine;
using AddressDefinition = Addressables.AddressDefinition;

namespace Atlas.Infrastructure
{
    public sealed class MasterDataService : IMasterDataService
    {
        private readonly IAssetLoader assetLoader;

        public MasterDataService(IAssetLoader assetLoader)
        {
            this.assetLoader = assetLoader;
        }

        public MemoryDatabase Database { get; private set; }

        public async UniTask LoadAsync()
        {
            using var handle = await assetLoader.LoadAssetAsync<TextAsset>(
                AddressDefinition.masterdatabytes, CancellationToken.None);
            Database = MasterDataLoader.Load(handle.Result.bytes);
        }
    }
}
