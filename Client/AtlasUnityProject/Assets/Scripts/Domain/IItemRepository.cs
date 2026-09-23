namespace Atlas.Domain
{
    // 所持アイテム(player_items)専用のリポジトリ。Pachimon/PachimonMoveMap/Partyの各リソースも
    // 同じ方針でそれぞれ専用のリポジトリを持つ(複数リソースをまとめた汎用Diff型は作らない)。
    public interface IItemRepository
    {
        // 未所持のアイテムはplayer_itemsに行が存在しない(=Repositoryにも無い)ためfalseを返す。
        bool TryGet(long itemId, out ItemEntity itemEntity);

        void Upsert(ItemEntity itemEntity);

        void Delete(long itemId);
    }
}
