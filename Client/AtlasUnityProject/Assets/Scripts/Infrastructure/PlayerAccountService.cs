using Atlas.Application;
using Atlas.Domain;

namespace Atlas.Infrastructure
{
    public sealed class PlayerAccountService : IPlayerAccountService
    {
        private readonly IPlayerProfileRepository profileRepository;
        private PlayerData playerData;

        public PlayerAccountService(IPlayerProfileRepository profileRepository)
        {
            this.profileRepository = profileRepository;
        }

        public PlayerData Get()
        {
            var profile = profileRepository.Get();
            return new PlayerData(profile.PlayerId, profile.Nickname);
        }
    }
}
