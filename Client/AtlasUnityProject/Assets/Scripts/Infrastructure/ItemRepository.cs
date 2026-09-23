using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Atlas.Domain;
using Cysharp.Threading.Tasks;
using Supplement.Core;

namespace Atlas.Infrastructure
{
    public sealed class ItemRepository : IItemRepository
    {
        private const string FileKey = "items";
        private const string Password = "atlas-items";

        private readonly IFileStorageService fileStorageService;
        private Dictionary<long, int> cache;

        public ItemRepository(IFileStorageService fileStorageService)
        {
            this.fileStorageService = fileStorageService;
        }

        public async UniTask<int> GetQuantityAsync(long itemId)
        {
            var items = await LoadAsync();
            return items.TryGetValue(itemId, out var quantity) ? quantity : 0;
        }

        public async UniTask ApplyAsync(IReadOnlyList<Item> upserted, IReadOnlyList<long> removed)
        {
            var items = await LoadAsync();

            foreach (var item in upserted)
            {
                items[item.ItemId] = item.Quantity;
            }

            foreach (var id in removed)
            {
                items.Remove(id);
            }

            await SaveAsync(items);
        }

        private async UniTask<Dictionary<long, int>> LoadAsync()
        {
            if (cache is not null)
            {
                return cache;
            }

            if (!fileStorageService.Exists(FileKey))
            {
                cache = new Dictionary<long, int>();
                return cache;
            }

            var dto = await fileStorageService.ReadAsync<ItemsDto>(FileKey, Password, CancellationToken.None);
            cache = dto.Items.ToDictionary(i => i.ItemId, i => i.Quantity);
            return cache;
        }

        private UniTask SaveAsync(Dictionary<long, int> items)
        {
            fileStorageService.CreateDirectoryIfNotExists(FileKey);
            var dto = new ItemsDto
            {
                Items = items.Select(kv => new ItemDto { ItemId = kv.Key, Quantity = kv.Value }).ToList(),
            };
            return fileStorageService.WriteAsync(FileKey, dto, Password, CancellationToken.None);
        }

        [System.Serializable]
        private sealed class ItemDto
        {
            public long ItemId;
            public int Quantity;
        }

        [System.Serializable]
        private sealed class ItemsDto
        {
            public List<ItemDto> Items = new();
        }
    }
}
