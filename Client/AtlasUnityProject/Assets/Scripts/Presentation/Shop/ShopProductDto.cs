namespace Atlas.Presentation.Shop
{
    public sealed class ShopProductDto
    {
        public string ItemName;
        public int Amount;
        public decimal UnitPrice;
        // 同じアイテムで一番少ない数の商品と比べた割引率(%)。0なら出さない。
        public int DiscountPercent;
        public int CostAmount;
        // 足りないジェムの数。0なら購入できる。
        public int ShortageAmount;
    }
}
