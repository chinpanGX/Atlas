using System.Text;
using Cysharp.Threading.Tasks;
using Supplement.Core;
using VolTech.Client.Auth;

namespace Atlas.Infrastructure
{
    // デバイス登録で受け取ったアカウント情報(device_id・secret_key・player_id)を、暗号化したファイルに保存する。
    // 接続先ごとにファイルを分ける。接続先を切り替えると、別の環境のDBには保存したdevice_idが無く、
    // トークンを発行できなくなるため。
    public sealed class FileAccountStore : IAccountStore
    {
        private const string Password = "atlas-account";

        private readonly IFileStorageService fileStorageService;
        private readonly string fileKey;

        public FileAccountStore(IFileStorageService fileStorageService, string serverAddress)
        {
            this.fileStorageService = fileStorageService;
            fileKey = "account-" + ToFileName(serverAddress);
        }

        public async UniTask<AccountData> LoadAsync()
        {
            if (!fileStorageService.Exists(fileKey))
            {
                return null;
            }

            var dto = await fileStorageService.ReadAsync<AccountDto>(fileKey, Password);
            return new AccountData(dto.DeviceId, dto.SecretKey, dto.PlayerId);
        }

        public UniTask SaveAsync(AccountData data)
        {
            fileStorageService.CreateDirectoryIfNotExists(fileKey);
            var dto = new AccountDto { DeviceId = data.DeviceId, SecretKey = data.SecretKey, PlayerId = data.PlayerId };
            return fileStorageService.WriteAsync(fileKey, dto, Password);
        }

        public UniTask ClearAsync()
        {
            return fileStorageService.Exists(fileKey) ? fileStorageService.DeleteFileAsync(fileKey) : UniTask.CompletedTask;
        }

        // URLの記号はファイル名に使えないものがあるので、英数字以外を'_'にする
        private static string ToFileName(string serverAddress)
        {
            var builder = new StringBuilder(serverAddress.Length);
            foreach (var c in serverAddress)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return builder.ToString();
        }

        [System.Serializable]
        private sealed class AccountDto
        {
            public string DeviceId;
            public string SecretKey;
            public string PlayerId;
        }
    }
}
