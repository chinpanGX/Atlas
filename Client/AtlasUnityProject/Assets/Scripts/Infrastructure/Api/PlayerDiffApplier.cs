using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    // サインイン・スカウト・パーティ編成・技の付け替えなど、playerDiffを返すAPIを実装する
    // 各Connection/Repository(同じくInfrastructure.Api配下)がレスポンス受信直後にここへ渡す。
    // リソース種別ごとのXxxDiffApplierへ振り分けるだけの窓口。
    public interface IPlayerDiffApplier
    {
        UniTask ApplyAsync(PlayerDiffDto dto);
    }

    public sealed class PlayerDiffApplier : IPlayerDiffApplier
    {
        private readonly ItemDiffApplier itemDiffApplier;
        private readonly PachimonDiffApplier pachimonDiffApplier;
        private readonly PachimonMoveMapDiffApplier pachimonMoveMapDiffApplier;
        private readonly PartyDiffApplier partyDiffApplier;

        public PlayerDiffApplier(
            ItemDiffApplier itemDiffApplier,
            PachimonDiffApplier pachimonDiffApplier,
            PachimonMoveMapDiffApplier pachimonMoveMapDiffApplier,
            PartyDiffApplier partyDiffApplier)
        {
            this.itemDiffApplier = itemDiffApplier;
            this.pachimonDiffApplier = pachimonDiffApplier;
            this.pachimonMoveMapDiffApplier = pachimonMoveMapDiffApplier;
            this.partyDiffApplier = partyDiffApplier;
        }

        public async UniTask ApplyAsync(PlayerDiffDto dto)
        {
            if (dto.Items != null)
                await itemDiffApplier.ApplyAsync(dto.Items);
            if (dto.Pachimon != null)
                await pachimonDiffApplier.ApplyAsync(dto.Pachimon);
            if (dto.PachimonMoveMap != null)
                await pachimonMoveMapDiffApplier.ApplyAsync(dto.PachimonMoveMap);
            if (dto.PartySlots != null)
                await partyDiffApplier.ApplyAsync(dto.PartySlots);
        }
    }
}
