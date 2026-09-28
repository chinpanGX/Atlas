using System.Threading;
using Atlas.Application.Address;
using Atlas.MasterData;
using Cysharp.Threading.Tasks;
using MasterMemory;
using Supplement.Loader.Abstractions;
using UnityEngine;

namespace Atlas.Infrastructure
{
    // マスタ(暗号化したmasterdata.bytes)をAddressablesから読み込み、読み込んだMemoryDatabaseを持つ。
    // 読み込みは起動時の初期化で1回だけ行う。MemoryDatabaseはDIでここから取り出して各所に渡す。
    public sealed class MasterDatabaseLoader
    {
        private readonly IAssetLoader assetLoader;

        public MasterDatabaseLoader(IAssetLoader assetLoader)
        {
            this.assetLoader = assetLoader;
        }

        // 読み込む前はnull。
        public MemoryDatabase Database { get; private set; }

        public async UniTask LoadAsync(CancellationToken cancellation)
        {
            // 復号した中身はMemoryDatabaseへ取り込むため、読み終えたら元のTextAssetは解放してよい。
            using var handle = await assetLoader.LoadAssetAsync<TextAsset>(AddressDefinition.masterdata, cancellation);
            Database = MasterDataLoader.Load(handle.Result.bytes);
        }
    }
}
