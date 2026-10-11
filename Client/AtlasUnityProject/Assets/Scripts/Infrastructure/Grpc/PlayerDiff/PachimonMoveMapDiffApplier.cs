using Atlas.Api.V1;
using Atlas.Domain;

namespace Atlas.Infrastructure.Grpc
{
    public sealed class PachimonMoveMapDiffApplier
    {
        private readonly IPachimonMoveMapRepository repository;

        public PachimonMoveMapDiffApplier(IPachimonMoveMapRepository repository)
        {
            this.repository = repository;
        }

        public void Apply(PachimonMoveMapDiff diff)
        {
            foreach (var move in diff.Upserted)
            {
                repository.Upsert(ToEntity(move));
            }
            foreach (var playerPachimonMoveId in diff.Removed)
            {
                repository.Delete(playerPachimonMoveId);
            }
        }

        public static PachimonMoveMapEntity ToEntity(PlayerPachimonMove move)
        {
            return new PachimonMoveMapEntity(move.PlayerPachimonMoveId, move.PlayerPachimonId, move.Slot, move.MoveId);
        }
    }
}
