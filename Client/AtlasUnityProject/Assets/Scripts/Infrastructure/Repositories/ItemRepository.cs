using System.Collections.Generic;
using Atlas.Application;
using Atlas.Domain;
using Supplement.Core;

namespace Atlas.Infrastructure
{
    public sealed class ItemRepository : IItemRepository
    {
        private readonly Dictionary<long, ItemEntity> itemEntities = new();
        private readonly IMessageBroker messageBroker;

        public ItemRepository(IMessageBroker messageBroker)
        {
            this.messageBroker = messageBroker;
        }

        public bool TryGet(long itemId, out ItemEntity itemEntity)
        {
            return itemEntities.TryGetValue(itemId, out itemEntity);
        }

        public void Upsert(ItemEntity itemEntity)
        {
            itemEntities[itemEntity.ItemId] = itemEntity;
            messageBroker.Publish(new ItemQuantityChanged());
        }

        public void Delete(long itemId)
        {
            itemEntities.Remove(itemId);
            messageBroker.Publish(new ItemQuantityChanged());
        }

        public void Clear()
        {
            itemEntities.Clear();
            messageBroker.Publish(new ItemQuantityChanged());
        }
    }
}
