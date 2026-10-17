namespace Atlas.Application
{
    // 1回の購入で ItemId を Amount 個受け取り、CostItemId を CostAmount 個払う。
    public sealed class ShopProduct
    {
        public readonly int ShopProductId;
        public readonly int ItemId;
        public readonly int Amount;
        public readonly int CostItemId;
        public readonly int CostAmount;

        public ShopProduct(int shopProductId, int itemId, int amount, int costItemId, int costAmount)
        {
            ShopProductId = shopProductId;
            ItemId = itemId;
            Amount = amount;
            CostItemId = costItemId;
            CostAmount = costAmount;
        }
    }
}
