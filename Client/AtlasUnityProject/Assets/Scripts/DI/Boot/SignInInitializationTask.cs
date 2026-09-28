using System.Threading;
using Atlas.Application;
using Cysharp.Threading.Tasks;
using Supplement.Core;

namespace Atlas.DI.Boot
{
    // デバイス認証(初回は登録)とサインイン。所持データはサインインのplayerDiffで各Repositoryに入る。
    internal sealed class SignInInitializationTask : IBootInitializationTask
    {
        private readonly ISignInService signInService;

        public SignInInitializationTask(ISignInService signInService)
        {
            this.signInService = signInService;
        }

        public InitializationPriority Priority => InitializationPriority.Normal;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            return signInService.SignInAsync();
        }
    }
}
