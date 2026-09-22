namespace Atlas.Infrastructure
{
    // ApiClient生成コードのFunc<string> accessTokenProviderに渡す、現在のアクセストークンの
    // 保持場所。AuthService.EnsureSignedUpAsync完了後に有効な値を返すようになる。
    public sealed class AccessTokenStore
    {
        public string CurrentToken { get; private set; }

        public void SetToken(string token)
        {
            CurrentToken = token;
        }
    }
}
