using Cysharp.Threading.Tasks;

namespace Atlas.Domain
{
    // PlayerProfileのローカル永続化(IDeviceCredentialsRepositoryと同じパターン)。
    public interface IPlayerProfileRepository
    {
        PlayerProfile Get();

        UniTask SaveAsync(PlayerProfile profile);
    }
}
