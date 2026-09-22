using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Mock
{
    public sealed class MockPlayerConnection : IPlayerConnection
    {
        public UniTask SignUpAsync(string nickname)
        {
            return UniTask.CompletedTask;
        }

        public UniTask<SignInResult> SignInAsync()
        {
            return UniTask.FromResult(new SignInResult("mock-player-id", "プレイヤー"));
        }
    }
}
