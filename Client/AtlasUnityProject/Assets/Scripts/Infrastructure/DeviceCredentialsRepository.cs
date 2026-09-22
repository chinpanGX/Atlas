using System.Threading;
using Atlas.Domain;
using Cysharp.Threading.Tasks;
using Supplement.Core;

namespace Atlas.Infrastructure
{
    public sealed class DeviceCredentialsRepository : IDeviceCredentialsRepository
    {
        private const string FileKey = "deviceCredentials";
        private const string Password = "atlas-device-credentials";

        private readonly IFileStorageService fileStorageService;

        public DeviceCredentialsRepository(IFileStorageService fileStorageService)
        {
            this.fileStorageService = fileStorageService;
        }

        public async UniTask<DeviceCredentials?> GetAsync()
        {
            if (!fileStorageService.Exists(FileKey))
            {
                return null;
            }

            var dto = await fileStorageService.ReadAsync<DeviceCredentialsDto>(FileKey, Password, CancellationToken.None);
            return new DeviceCredentials(dto.DeviceId, dto.SecretKey);
        }

        public UniTask SaveAsync(DeviceCredentials credentials)
        {
            fileStorageService.CreateDirectoryIfNotExists(FileKey);
            var dto = new DeviceCredentialsDto { DeviceId = credentials.DeviceId, SecretKey = credentials.SecretKey };
            return fileStorageService.WriteAsync(FileKey, dto, Password, CancellationToken.None);
        }

        [System.Serializable]
        private sealed class DeviceCredentialsDto
        {
            public string DeviceId;
            public string SecretKey;
        }
    }
}
