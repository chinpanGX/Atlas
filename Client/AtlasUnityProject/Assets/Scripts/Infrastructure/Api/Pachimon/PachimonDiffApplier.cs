using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    public sealed class PachimonDiffApplier
    {
        private readonly IPachimonRepository repository;

        public PachimonDiffApplier(IPachimonRepository repository)
        {
            this.repository = repository;
        }

        public UniTask ApplyAsync(PachimonDiffDto dto)
        {
            foreach (var pachimon in dto.Upserted)
            {
                repository.Upsert(new PachimonEntity(pachimon.PlayerPachimonId, pachimon.PachimonId));
            }
            foreach (var playerPachimonId in dto.Removed)
            {
                repository.Delete(playerPachimonId);
            }
            return UniTask.CompletedTask;
        }
    }
}
