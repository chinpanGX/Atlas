using System.Threading;
using Atlas.Domain;
using Atlas.Infrastructure;
using Cysharp.Threading.Tasks;
using Supplement.Core;
using Supplement.Loader.Abstractions;
using Supplement.ZeroMessenger;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation
{
    /// <summary>
    /// Bootstrapシーンの常駐スコープ。design/client-architecture.md「シーン構成」参照。
    /// アプリ生存期間中ずっと存在し、Home/BattleシーンのLifetimeScopeの親になる。
    /// 各IXxxConnectionのMock/Real登録もここで行う(構成ルートとしての例外的な参照、
    /// Presenter自身はAtlas.Infrastructureを直接参照しない)。
    /// </summary>
    public sealed class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterAddressablesLoader();
            builder.Register<IMessageBroker, GlobalMessageBroker>(Lifetime.Singleton);
            // Real実装が無いため現状Mock固定。ConnectionConfigによるMock/Real切り替えは
            // design/client-architecture.md「切り替え方法」参照、Real実装時に追加する
            builder.Register<IPlayerConnection, MockPlayerConnection>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BootstrapEntryPoint>();
        }
    }

    internal sealed class BootstrapEntryPoint : IAsyncStartable
    {
        private readonly ISceneLoader sceneLoader;
        private readonly LifetimeScope rootScope;

        public BootstrapEntryPoint(ISceneLoader sceneLoader, LifetimeScope rootScope)
        {
            this.sceneLoader = sceneLoader;
            this.rootScope = rootScope;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            using (LifetimeScope.EnqueueParent(rootScope))
            {
                await sceneLoader.ChangeScene("Home", true, cancellation);
            }
        }
    }
}
