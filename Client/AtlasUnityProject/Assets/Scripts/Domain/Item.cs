namespace Atlas.Domain
{
    public class ItemEntity
    {
        public readonly long ItemId;
        public readonly int Quantity;
        
        public ItemEntity(long itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }
}
