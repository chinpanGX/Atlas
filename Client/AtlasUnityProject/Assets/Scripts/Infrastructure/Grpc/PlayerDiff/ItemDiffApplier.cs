using Atlas.Api.V1;
using Atlas.Domain;

namespace Atlas.Infrastructure.Grpc
{
    public sealed class ItemDiffApplier
    {
        private readonly IItemRepository repository;

        public ItemDiffApplier(IItemRepository repository)
        {
            this.repository = repository;
        }

        public void Apply(ItemsDiff diff)
        {
            foreach (var item in diff.Upserted)
            {
                repository.Upsert(ToEntity(item));
            }
            foreach (var itemId in diff.Removed)
            {
                repository.Delete(itemId);
            }
        }

        public static ItemEntity ToEntity(PlayerItem item)
        {
            return new ItemEntity(item.ItemId, item.Quantity);
        }
    }
}
