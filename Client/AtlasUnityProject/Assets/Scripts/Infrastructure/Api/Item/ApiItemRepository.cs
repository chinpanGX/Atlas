using System.Collections.Generic;
using Atlas.Domain;

namespace Atlas.Infrastructure.Api
{
    public sealed class ApiItemRepository : IItemRepository
    {
        private readonly Dictionary<long, ItemEntity> itemEntities = new();
        
        public int GetQuantity(long itemId)
        {
            if (!itemEntities.TryGetValue(itemId, out var entity))
            {
                return 0;
            }
            return entity.Quantity;
        }
        
        public void Upsert(ItemEntity itemEntity)
        {
            itemEntities[itemEntity.ItemId] = itemEntity;
        }
        
        public void Delete(long itemId)
        {
            itemEntities.Remove(itemId);
        }
    }
}
