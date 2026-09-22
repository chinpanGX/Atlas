using System.Threading;
using Atlas.Navigation;
using Atlas.Presentation.Home;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;
using VContainer;
using VContainer.Unity;

namespace Atlas.DI
{
    public sealed class HomeLifetimeScope : LifetimeScope
    {
        [SerializeField] private PageContainer pageContainer;
        [SerializeField] private ModalContainer modalContainer;

        // BootstrapEntryPoint.StartAsync内のLifetimeScope.EnqueueParent(rootScope)は
        // ChangeScene()のawait完了(=シーンLoad完了)時点で解除されるが、これは自分自身の
        // Awake()完了より後になるとは限らない。EnqueueParent(rootScope)がまだ有効な間に
        // このシーン自身のHomeEntryPointが(Awake→Build内で同期的に走るため)HomePageの
        // PushPageAsyncを開始し、その内部でさらにLifetimeScope.EnqueueParent(this)を
        // ネストして積むため、外側(rootScope)がAddressablesのシーンLoad完了を検知して
        // 先にPopしてしまうと、後入れのthisが先に取り除かれてスタックが壊れる
        // (HomePage側のBuild()がRootLifetimeScopeを親と誤認する不具合が実際に発生した)。
        // EnqueueParentの共有スタックに依存せず、常にRootLifetimeScopeを直接探すことで
        // このタイミング依存を無くす。
        protected override LifetimeScope FindParent() => Find<RootLifetimeScope>();

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(pageContainer);
            builder.RegisterComponent(modalContainer);
            // LifetimeScope自身はVContainerが自動的にRegisterInstance<LifetimeScope>(this)する
            // (LifetimeScope.cs参照)ため、ここで明示的に登録する必要はない
            builder.Register<IScreenNavigator, ScreenNavigator>(Lifetime.Singleton);
            builder.RegisterEntryPoint<HomeEntryPoint>();
        }
    }

    internal sealed class HomeEntryPoint : IAsyncStartable
    {
        private readonly IScreenNavigator screenNavigator;

        public HomeEntryPoint(IScreenNavigator screenNavigator)
        {
            this.screenNavigator = screenNavigator;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            await screenNavigator.PushPageAsync<HomePage>();
        }
    }
}
