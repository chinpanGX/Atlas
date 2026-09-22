using Cysharp.Threading.Tasks;
using MasterMemory;

namespace Atlas.Application
{
    public interface IMasterDataService
    {
        MemoryDatabase Database { get; }

        UniTask LoadAsync();
    }
}
