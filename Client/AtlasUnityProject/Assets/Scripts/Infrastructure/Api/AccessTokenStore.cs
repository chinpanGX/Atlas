using System;

namespace Atlas.Infrastructure.Api
{
    // ApiClient生成コードのFunc<string> accessTokenProviderに渡す、現在のアクセストークンの
    // 保持場所。書き込みはAccessTokenRefresherのみが行う(SignInService.SignInAsyncでの初回認証、
    // および以降の期限前再認証・401時の再認証)。
    public sealed class AccessTokenStore
    {
        public string CurrentToken { get; private set; }

        // サーバーのexpires_at(サーバー時刻)ではなく、受信時点の端末時刻 + expiresInで
        // 算出するため、端末とサーバーの時計ずれの影響を受けない。未認証時はDateTime.MinValue。
        public DateTime ExpiresAtUtc { get; private set; } = DateTime.MinValue;

        public void SetToken(string token, long expiresInSeconds)
        {
            CurrentToken = token;
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);
        }

        // 残り有効期間がmargin未満(期限切れ・未認証を含む)であればtrue。
        public bool IsExpiringWithin(TimeSpan margin)
        {
            return ExpiresAtUtc - DateTime.UtcNow < margin;
        }
    }
}
