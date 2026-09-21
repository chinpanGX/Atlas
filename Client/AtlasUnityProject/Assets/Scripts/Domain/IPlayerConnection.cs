using System.Threading;
using Cysharp.Threading.Tasks;

namespace Atlas.Domain
{
    /// <summary>
    /// design/outgame.md「5. 自分のプレイヤー情報取得」(GET /players/me)に対応するConnection抽象。
    /// design/client-architecture.md「コア進行ロジックのMock/Real切り替え」参照。
    /// </summary>
    public interface IPlayerConnection
    {
        UniTask<PlayerData> GetMeAsync(CancellationToken token);
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
