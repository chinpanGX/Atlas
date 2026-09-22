using System.Collections.Generic;
using Atlas.Domain;

namespace Atlas.Infrastructure.Api
{
    public sealed class ApiItemRepository : IItemRepository
    {
        private readonly Dictionary<long, int> itemEntities = new();
        
        public int GetQuantity(long itemId)
        {
            return itemEntities.GetValueOrDefault(itemId, 0);
        }
        
        public void Upsert(ItemEntity itemEntity)
        {
            itemEntities.Add(itemEntity.ItemId, GetQuantity(itemEntity.ItemId));
        }
        
        public void Delete(long itemId)
        {
            itemEntities.Remove(itemId);
        }
    }
}
