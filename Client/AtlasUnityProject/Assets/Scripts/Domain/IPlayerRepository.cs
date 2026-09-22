using Cysharp.Threading.Tasks;

namespace Atlas.Domain
{
    public interface IPlayerRepository
    {
        UniTask<PlayerData> GetAsync();

        UniTask<PlayerData> CreateAsync(string nickname);
    }

    public readonly struct PlayerData
    {
        public readonly string PlayerId;
        public readonly string Nickname;
        public readonly int Gems;

        public PlayerData(string playerId, string nickname, int gems)
        {
            PlayerId = playerId;
            Nickname = nickname;
            Gems = gems;
        }
    }
}
