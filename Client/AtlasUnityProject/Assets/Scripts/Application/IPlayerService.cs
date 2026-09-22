using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // Presenterが直接依存するアプリケーションサービス抽象。IPlayerRepositoryへの薄いパススルー
    public interface IPlayerService
    {
        UniTask<PlayerData> GetMeAsync();
    }
}
