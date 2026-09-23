using System;
using Atlas.Application;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    // アクセストークンの発行・再発行をまとめて担う(design/outgame.md「デバイス認証」補足)。
    // リフレッシュトークンは無く、device_id + secret_keyによるPOST /devices/authenticateの
    // 再実行がリフレッシュを兼ねる。
    //
    // 要認証APIを叩く各Connectionは、生成ApiClientの呼び出しをSendAsyncで包む。SendAsyncは
    // 1. 送信前に端末内で有効期限をチェックし(通信なし)、期限が近ければ再認証してから送る
    // 2. それでも401が返った場合(バックグラウンド中の失効等)は、再認証して1回だけリトライする
    //
    // サーバーは再認証時に古いトークンを即無効化するため、再認証が並行して走ると互いのトークンを
    // 無効化し合う。これを避けるため、実行中の再認証があればそれを共有して待つ(single-flight)。
    // UnityWebRequest/UniTaskの継続はメインスレッドで動くため、ロックは使わない。
    public sealed class AccessTokenRefresher
    {
        // 残り有効期間がこれを下回ったら送信前に再認証する。サーバーのTTL(3600秒)に対して、
        // 送信〜サーバー到達までの遅延や端末時刻の多少のずれを吸収できるだけの余裕を持たせる。
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

        private readonly IDeviceCredentialsRepository credentialsRepository;
        private readonly IDeviceConnection deviceConnection;
        private readonly AccessTokenStore accessTokenStore;

        private UniTask refreshTask;
        private bool isRefreshing;

        public AccessTokenRefresher(
            IDeviceCredentialsRepository credentialsRepository,
            IDeviceConnection deviceConnection,
            AccessTokenStore accessTokenStore)
        {
            this.credentialsRepository = credentialsRepository;
            this.deviceConnection = deviceConnection;
            this.accessTokenStore = accessTokenStore;
        }

        // 保存済みのデバイス資格情報で認証し、AccessTokenStoreを新しいトークンに差し替える。
        // 実行中の再認証があれば新たに通信せず、その完了を待つ。
        public UniTask RefreshAsync()
        {
            if (!isRefreshing)
            {
                isRefreshing = true;
                // UniTaskは複数回awaitできないため、並行する呼び出し元で共有できるようPreserveする。
                refreshTask = RefreshCoreAsync().Preserve();
            }

            return refreshTask;
        }

        public async UniTask<TResponse> SendAsync<TResponse>(Func<UniTask<TResponse>> request)
        {
            await EnsureValidTokenAsync();
            var sentToken = accessTokenStore.CurrentToken;
            try
            {
                return await request();
            }
            catch (ApiException e) when (e.StatusCode == 401)
            {
                await RefreshAfterUnauthorizedAsync(sentToken);
                return await request();
            }
        }

        public async UniTask SendAsync(Func<UniTask> request)
        {
            await EnsureValidTokenAsync();
            var sentToken = accessTokenStore.CurrentToken;
            try
            {
                await request();
            }
            catch (ApiException e) when (e.StatusCode == 401)
            {
                await RefreshAfterUnauthorizedAsync(sentToken);
                await request();
            }
        }

        private UniTask EnsureValidTokenAsync()
        {
            return accessTokenStore.IsExpiringWithin(RefreshMargin) ? RefreshAsync() : UniTask.CompletedTask;
        }

        // 送信に使ったトークンが既に(並行する別リクエストの再認証で)差し替わっていれば、
        // 再認証せずそのままリトライする。ここで再認証すると差し替わったばかりのトークンを
        // 無効化してしまうため。
        private UniTask RefreshAfterUnauthorizedAsync(string sentToken)
        {
            return sentToken == accessTokenStore.CurrentToken ? RefreshAsync() : UniTask.CompletedTask;
        }

        private async UniTask RefreshCoreAsync()
        {
            try
            {
                var credentials = await credentialsRepository.GetAsync();
                if (credentials is null)
                {
                    // SignInService.SignInAsyncで資格情報を保存してから呼ばれるため通常は起こらない。
                    throw new InvalidOperationException("デバイス資格情報が保存されていません");
                }

                var authResult = await deviceConnection.AuthenticateAsync(
                    credentials.Value.DeviceId, credentials.Value.SecretKey);
                accessTokenStore.SetToken(authResult.AccessToken, authResult.ExpiresIn);
            }
            finally
            {
                isRefreshing = false;
            }
        }
    }
}
