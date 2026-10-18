namespace Atlas.Domain
{
    // 所持アイテム(player_items)専用のリポジトリ。Pachimon/PachimonMoveMap/Partyの各リソースも
    // 同じ方針でそれぞれ専用のリポジトリを持つ(複数リソースをまとめた汎用Diff型は作らない)。
    public interface IItemRepository
    {
        // 一度も手に入れていないアイテムはplayer_itemsに行が無い(=Repositoryにも無い)ためfalseを返す。
        // 使って0になったアイテムはサーバーが行を消さないので、所持数0でtrueを返す。
        bool TryGet(long itemId, out ItemEntity itemEntity);

        void Upsert(ItemEntity itemEntity);

        void Delete(long itemId);

        // サインインし直すとき(タイトルに戻ったとき)に、サーバーで無くなったものが残らないよう全件を消す。
        void Clear();
    }
}
