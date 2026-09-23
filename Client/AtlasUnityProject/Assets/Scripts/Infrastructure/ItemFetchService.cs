using Atlas.Application;
using Atlas.Domain;

namespace Atlas.Infrastructure
{
    public sealed class ItemFetchService : IItemFetchService
    {
        private readonly IItemRepository itemRepository;

        public ItemFetchService(IItemRepository itemRepository)
        {
            this.itemRepository = itemRepository;
        }

        public int GetAmount(int itemId)
        {
            return itemRepository.TryGet(itemId, out var itemEntity) ? itemEntity.Quantity : 0;
        }
    }
}
