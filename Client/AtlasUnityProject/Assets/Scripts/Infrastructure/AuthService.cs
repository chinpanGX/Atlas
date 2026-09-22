using System;
using Atlas.Application;
using Atlas.Domain;
using Atlas.Infrastructure.Api;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class AuthService : IAuthService
    {
        // ニックネーム入力画面は未実装のため固定値を使う(design/outgame.md「プレイヤー作成」参照)。
        private const string DefaultNickname = "プレイヤー";

        private readonly IDeviceCredentialsRepository credentialsRepository;
        private readonly IDeviceConnection deviceConnection;
        private readonly IPlayerRepository playerRepository;
        private readonly AccessTokenStore accessTokenStore;

        public AuthService(
            IDeviceCredentialsRepository credentialsRepository,
            IDeviceConnection deviceConnection,
            IPlayerRepository playerRepository,
            AccessTokenStore accessTokenStore)
        {
            this.credentialsRepository = credentialsRepository;
            this.deviceConnection = deviceConnection;
            this.playerRepository = playerRepository;
            this.accessTokenStore = accessTokenStore;
        }

        // outgame.mdの補足にある「早めの再認証」「401時の再認証リトライ」は未実装で、
        // 起動時に一度だけ認証する疎通確認レベルの実装(残タスクはprogress.md参照)。
        public async UniTask EnsureSignedUpAsync()
        {
            var credentials = await credentialsRepository.GetAsync();
            if (credentials is null)
            {
                var secretKey = Guid.NewGuid().ToString("N");
                var deviceId = await deviceConnection.RegisterAsync(secretKey);
                credentials = new DeviceCredentials(deviceId, secretKey);
                await credentialsRepository.SaveAsync(credentials.Value);
            }

            var authResult = await deviceConnection.AuthenticateAsync(credentials.Value.DeviceId, credentials.Value.SecretKey);
            accessTokenStore.SetToken(authResult.AccessToken);

            try
            {
                await playerRepository.GetAsync();
            }
            catch (ApiException e) when (e.StatusCode == 404)
            {
                await playerRepository.CreateAsync(DefaultNickname);
            }
        }
    }
}
