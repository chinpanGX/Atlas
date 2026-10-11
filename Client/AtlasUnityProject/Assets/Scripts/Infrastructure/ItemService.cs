using Atlas.Application;
using Atlas.Domain;

namespace Atlas.Infrastructure
{
    internal sealed class ItemService : IItemService
    {
        private readonly IItemRepository itemRepository;

        public ItemService(IItemRepository itemRepository)
        {
            this.itemRepository = itemRepository;
        }

        public int GetAmount(int itemId)
        {
            return itemRepository.TryGet(itemId, out var itemEntity) ? itemEntity.Quantity : 0;
        }
    }
}
