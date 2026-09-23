using System.Collections.Generic;
using Atlas.Domain;

namespace Atlas.Infrastructure.Api
{
    public sealed class ApiItemRepository : IItemRepository
    {
        private readonly Dictionary<long, ItemEntity> itemEntities = new();
        
        public bool TryGet(long itemId, out ItemEntity itemEntity)
        {
            return itemEntities.TryGetValue(itemId, out itemEntity);
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
