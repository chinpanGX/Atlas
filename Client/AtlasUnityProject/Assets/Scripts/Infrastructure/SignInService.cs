using System;
using Atlas.Application;
using Atlas.Domain;
using Atlas.Infrastructure.Api;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class SignInService : ISignInService
    {
        // ニックネーム入力画面は未実装のため固定値を使う(design/outgame.md「サインアップ」参照)。
        private const string DefaultNickname = "プレイヤー";

        private readonly IDeviceCredentialsRepository credentialsRepository;
        private readonly IDeviceConnection deviceConnection;
        private readonly IPlayerConnection playerConnection;
        private readonly IPlayerProfileRepository playerProfileRepository;
        private readonly AccessTokenRefresher accessTokenRefresher;

        public SignInService(
            IDeviceCredentialsRepository credentialsRepository,
            IDeviceConnection deviceConnection,
            IPlayerConnection playerConnection,
            IPlayerProfileRepository playerProfileRepository,
            AccessTokenRefresher accessTokenRefresher)
        {
            this.credentialsRepository = credentialsRepository;
            this.deviceConnection = deviceConnection;
            this.playerConnection = playerConnection;
            this.playerProfileRepository = playerProfileRepository;
            this.accessTokenRefresher = accessTokenRefresher;
        }

        // 初回認証もAccessTokenRefresher経由で行い、トークンの書き込み経路を一本化する
        // (以降の期限前再認証・401時の再認証リトライは各ConnectionがAccessTokenRefresher経由で行う)。
        // playerDiffの適用(items等)はIPlayerConnection.SignInAsync内部(Infrastructure.Api)で
        // 完結しており、ここでは行わない。
        public async UniTask SignInAsync()
        {
            var credentials = await credentialsRepository.GetAsync();
            if (credentials is null)
            {
                var secretKey = Guid.NewGuid().ToString("N");
                var deviceId = await deviceConnection.RegisterAsync(secretKey);
                credentials = new DeviceCredentials(deviceId, secretKey);
                await credentialsRepository.SaveAsync(credentials.Value);
            }

            await accessTokenRefresher.RefreshAsync();

            SignInResult signIn;
            try
            {
                signIn = await playerConnection.SignInAsync();
            }
            catch (ApiException e) when (e.StatusCode == 404)
            {
                await playerConnection.SignUpAsync(DefaultNickname);
                signIn = await playerConnection.SignInAsync();
            }

            await playerProfileRepository.SaveAsync(new PlayerProfile(signIn.PlayerId, signIn.Nickname));
        }
    }
}
