using Atlas.Api.V1;
using Atlas.Domain;

namespace Atlas.Infrastructure.Grpc
{
    public sealed class PachimonDiffApplier
    {
        private readonly IPachimonRepository repository;

        public PachimonDiffApplier(IPachimonRepository repository)
        {
            this.repository = repository;
        }

        public void Apply(PachimonDiff diff)
        {
            foreach (var pachimon in diff.Upserted)
            {
                repository.Upsert(ToEntity(pachimon));
            }
            foreach (var playerPachimonId in diff.Removed)
            {
                repository.Delete(playerPachimonId);
            }
        }

        public static PachimonEntity ToEntity(PlayerPachimon pachimon)
        {
            return new PachimonEntity(pachimon.PlayerPachimonId, pachimon.PachimonId);
        }
    }
}
