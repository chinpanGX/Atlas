using Atlas.Api.V1;
using Cysharp.Threading.Tasks;
using Google.Protobuf;

namespace Atlas.Infrastructure.Grpc
{
    // player_diff をリソースごとの Applier に振り分ける。変わっていないリソースは null で届くので飛ばす。
    // 通信の共通処理(ApiCore)が、成功したレスポンスを呼び出し側に返す前に呼ぶ。
    public sealed class PlayerDiffApplier : VolTech.Client.IPlayerDiffApplier
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

        public UniTask ApplyAsync(IMessage response)
        {
            if (response is IHasPlayerDiff { PlayerDiff: { } diff })
            {
                Apply(diff);
            }
            return UniTask.CompletedTask;
        }

        private void Apply(PlayerDiff diff)
        {
            if (diff.Items != null)
            {
                itemDiffApplier.Apply(diff.Items);
            }
            if (diff.Pachimon != null)
            {
                pachimonDiffApplier.Apply(diff.Pachimon);
            }
            if (diff.PachimonMoveMap != null)
            {
                pachimonMoveMapDiffApplier.Apply(diff.PachimonMoveMap);
            }
            if (diff.PartySlots != null)
            {
                partyDiffApplier.Apply(diff.PartySlots);
            }
        }
    }
}
