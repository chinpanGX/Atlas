namespace Atlas.Domain
{
    // 所持アイテム(player_items)専用のリポジトリ。Pachimon/PachimonMoveMap/Partyの各リソースも
    // 同じ方針でそれぞれ専用のリポジトリを持つ(複数リソースをまとめた汎用Diff型は作らない)。
    public interface IItemRepository
    {
        int GetQuantity(long itemId);
        
        void Upsert(ItemEntity itemEntity);

        void Delete(long itemId);
    }
}
