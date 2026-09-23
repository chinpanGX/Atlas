using System.Threading;
using Atlas.Domain;
using Cysharp.Threading.Tasks;
using Supplement.Core;

namespace Atlas.Infrastructure
{
    public sealed class PlayerProfileRepository : IPlayerProfileRepository
    {
        private const string FileKey = "playerProfile";
        private const string Password = "atlas-player-profile";

        private readonly IFileStorageService fileStorageService;

        public PlayerProfileRepository(IFileStorageService fileStorageService)
        {
            this.fileStorageService = fileStorageService;
        }

        public async UniTask<PlayerProfile?> GetAsync()
        {
            if (!fileStorageService.Exists(FileKey))
            {
                return null;
            }

            var dto = await fileStorageService.ReadAsync<PlayerProfileDto>(FileKey, Password, CancellationToken.None);
            return new PlayerProfile(dto.PlayerId, dto.Nickname);
        }

        public UniTask SaveAsync(PlayerProfile profile)
        {
            fileStorageService.CreateDirectoryIfNotExists(FileKey);
            var dto = new PlayerProfileDto { PlayerId = profile.PlayerId, Nickname = profile.Nickname };
            return fileStorageService.WriteAsync(FileKey, dto, Password, CancellationToken.None);
        }

        [System.Serializable]
        private sealed class PlayerProfileDto
        {
            public string PlayerId;
            public string Nickname;
        }
    }
}
