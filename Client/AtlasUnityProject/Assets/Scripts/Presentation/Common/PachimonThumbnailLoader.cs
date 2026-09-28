using System;
using System.Threading;
using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using Supplement.Loader.Abstractions;
using UnityEngine;

namespace Atlas.Presentation.Common
{
    // 1つの表示枠のサムネイルを読み込み、次を読み込むとき・Disposeのときに前のものを解放する。
    // 読み込み中に次のLoadやDisposeが来たら、遅れて届いた結果は使わずに解放する。
    // 取り消しはIAssetLoaderに渡さない(渡すと取り消しのたびに警告ログが出るため)。
    public sealed class PachimonThumbnailLoader : IDisposable
    {
        private readonly IAssetLoader assetLoader;
        private IAssetHandle<Sprite> handle;
        // Load・Clearのたびに増やし、読み込み完了時に自分が最新の要求かを確かめる。
        private int requestVersion;
        private bool disposed;

        public PachimonThumbnailLoader(IAssetLoader assetLoader)
        {
            this.assetLoader = assetLoader;
        }

        // 読み込めたらonLoadedにSpriteを渡す。読み込めなかったときは呼ばない(呼び出し側のプレースホルダのまま)。
        public void Load(long pachimonId, Action<Sprite> onLoaded)
        {
            Clear();
            LoadAsync(pachimonId, onLoaded, requestVersion).Forget();
        }

        public void Clear()
        {
            requestVersion++;
            handle?.Dispose();
            handle = null;
        }

        public void Dispose()
        {
            disposed = true;
            Clear();
        }

        private async UniTaskVoid LoadAsync(long pachimonId, Action<Sprite> onLoaded, int version)
        {
            IAssetHandle<Sprite> loaded;
            try
            {
                loaded = await assetLoader.LoadAssetAsync<Sprite>(AddressDefinition.GetThumb(pachimonId), CancellationToken.None);
            }
            catch (Exception)
            {
                // 失敗の内容はIAssetLoaderがログに出している。
                return;
            }

            if (disposed || version != requestVersion)
            {
                loaded.Dispose();
                return;
            }

            handle = loaded;
            onLoaded(loaded.Result);
        }
    }
}
