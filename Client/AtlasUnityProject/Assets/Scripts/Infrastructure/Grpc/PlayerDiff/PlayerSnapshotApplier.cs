using Atlas.Api.V1;
using Atlas.Domain;

namespace Atlas.Infrastructure.Grpc
{
    // サインインで受け取った所持データの全件で、手元の所持データを置き換える。
    // 全件には removed が無いので、先に消さないとサーバーで無くなったものが残る。
    public sealed class PlayerSnapshotApplier
    {
        private readonly IItemRepository itemRepository;
        private readonly IPachimonRepository pachimonRepository;
        private readonly IPachimonMoveMapRepository pachimonMoveMapRepository;
        private readonly IPartyRepository partyRepository;

        public PlayerSnapshotApplier(
            IItemRepository itemRepository,
            IPachimonRepository pachimonRepository,
            IPachimonMoveMapRepository pachimonMoveMapRepository,
            IPartyRepository partyRepository)
        {
            this.itemRepository = itemRepository;
            this.pachimonRepository = pachimonRepository;
            this.pachimonMoveMapRepository = pachimonMoveMapRepository;
            this.partyRepository = partyRepository;
        }

        public void Replace(PlayerSnapshot snapshot)
        {
            itemRepository.Clear();
            foreach (var item in snapshot.Items)
            {
                itemRepository.Upsert(ItemDiffApplier.ToEntity(item));
            }

            pachimonRepository.Clear();
            foreach (var pachimon in snapshot.Pachimon)
            {
                pachimonRepository.Upsert(PachimonDiffApplier.ToEntity(pachimon));
            }

            pachimonMoveMapRepository.Clear();
            foreach (var move in snapshot.PachimonMoves)
            {
                pachimonMoveMapRepository.Upsert(PachimonMoveMapDiffApplier.ToEntity(move));
            }

            partyRepository.Clear();
            foreach (var partySlot in snapshot.PartySlots)
            {
                partyRepository.Upsert(PartyDiffApplier.ToEntity(partySlot));
            }
        }
    }
}
