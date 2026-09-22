namespace Atlas.Application
{
    public readonly struct PlayerData
    {
        public readonly string PlayerId;
        public readonly string Nickname;

        public PlayerData(string playerId, string nickname)
        {
            PlayerId = playerId;
            Nickname = nickname;
        }
    }
}
