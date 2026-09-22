using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    // playerId/nicknameの実データはサーバーのDB(players)が正であり、サインインし直せば
    // いつでも再取得できるため、ApiItemRepositoryと同様にローカルディスクへは永続化せず
    // メモリに保持するだけにする。
    public sealed class ApiPlayerProfileRepository : IPlayerProfileRepository
    {
        private PlayerProfile profile;

        public PlayerProfile Get()
        {
            return profile;
        }
        
        public UniTask SaveAsync(PlayerProfile newProfile)
        {
            profile = newProfile;
            return UniTask.CompletedTask;
        }
    }
}
