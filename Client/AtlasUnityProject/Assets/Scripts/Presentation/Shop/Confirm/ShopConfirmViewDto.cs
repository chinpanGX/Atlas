namespace Atlas.Presentation.Shop
{
    // 購入する前と後の所持数を並べて出すための値。
    public sealed class ShopConfirmViewDto
    {
        public string ItemName;
        public int Amount;
        public string CostItemName;
        public int CostAmount;
        public int CostItemBefore;
        public int CostItemAfter;
        public int ItemBefore;
        public int ItemAfter;
    }
}
