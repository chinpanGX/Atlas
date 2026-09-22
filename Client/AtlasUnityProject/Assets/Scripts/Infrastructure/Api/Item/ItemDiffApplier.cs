using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    public sealed class ItemDiffApplier
    {
        private readonly IItemRepository repository;

        public ItemDiffApplier(IItemRepository repository)
        {
            this.repository = repository;
        }

        public UniTask ApplyAsync(ItemsDiffDto dto)
        {
            if (dto.Upserted.Count != 0)
            {
                foreach (var item in dto.Upserted)
                {
                    repository.Upsert(new ItemEntity(item.ItemId, item.Quantity));
                }
            }
            if (dto.Removed.Count != 0)
            {
                foreach (var itemId in dto.Removed)
                {
                    repository.Delete(itemId);
                }
            }
            return UniTask.CompletedTask;
        }
    }
}