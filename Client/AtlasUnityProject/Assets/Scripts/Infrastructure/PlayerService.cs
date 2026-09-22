using Atlas.Application;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class PlayerService : IPlayerService
    {
        private readonly IPlayerRepository repository;

        public PlayerService(IPlayerRepository repository)
        {
            this.repository = repository;
        }

        public UniTask<PlayerData> GetMeAsync()
        {
            return repository.GetMeAsync();
        }
    }
}
