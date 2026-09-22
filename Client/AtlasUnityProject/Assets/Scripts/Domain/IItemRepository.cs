namespace Atlas.Domain
{
    // 所持アイテム(player_items)専用のリポジトリ。Pachimon/PachimonAssignment/Partyの各リソースも
    // Scout/Party画面実装時(progress.md残タスク#9)に同じ方針でそれぞれ専用のリポジトリを作る
    // (複数リソースをまとめた汎用Diff型は作らない)。
    public interface IItemRepository
    {
        int GetQuantity(long itemId);
        
        void Upsert(ItemEntity itemEntity);

        void Delete(long itemId);
    }
}
