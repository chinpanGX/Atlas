using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    public sealed class PachimonMoveMapDiffApplier
    {
        private readonly IPachimonMoveMapRepository repository;

        public PachimonMoveMapDiffApplier(IPachimonMoveMapRepository repository)
        {
            this.repository = repository;
        }

        public UniTask ApplyAsync(PachimonMoveMapDiffDto dto)
        {
            foreach (var move in dto.Upserted)
            {
                repository.Upsert(new PachimonMoveMapEntity(
                    move.PlayerPachimonMoveId, move.PlayerPachimonId, move.Slot, move.MoveId));
            }
            foreach (var playerPachimonMoveId in dto.Removed)
            {
                repository.Delete(playerPachimonMoveId);
            }
            return UniTask.CompletedTask;
        }
    }
}
