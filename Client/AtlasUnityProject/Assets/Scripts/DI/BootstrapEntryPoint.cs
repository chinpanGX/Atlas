using System.Threading;
using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using Supplement.Core;
using UnityScreenNavigator;
using VContainer.Unity;

namespace Atlas.DI
{
    internal sealed class BootstrapEntryPoint : IAsyncStartable
    {
        private readonly ISceneNavigator sceneNavigator;
        private readonly BootInitializer bootInitializer;

        public BootstrapEntryPoint(ISceneNavigator sceneNavigator, BootInitializer bootInitializer)
        {
            this.sceneNavigator = sceneNavigator;
            this.bootInitializer = bootInitializer;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            await bootInitializer.RunAsync(cancellation);

            // HomeLifetimeScope.FindParentが直接RootLifetimeScopeを探すため、ここで
            // LifetimeScope.EnqueueParentを使う必要は無い。
            await sceneNavigator.ChangeSceneAsync(AddressDefinition.Home, cancellation);
        }
    }
}