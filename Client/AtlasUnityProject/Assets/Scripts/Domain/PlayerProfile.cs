namespace Atlas.Domain
{
    // サインイン(IPlayerConnection.SignInAsync、ISignInService経由)で確定するプレイヤー識別情報。
    // playerIdは発行後変わらず、nicknameも変更手段が今のところ無いため、常に現在値として扱う。
    public readonly struct PlayerProfile
    {
        public readonly string PlayerId;
        public readonly string Nickname;

        public PlayerProfile(string playerId, string nickname)
        {
            PlayerId = playerId;
            Nickname = nickname;
        }
    }
}
