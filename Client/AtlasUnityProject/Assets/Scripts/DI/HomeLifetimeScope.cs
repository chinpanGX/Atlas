using System.Threading;
using Atlas.Presentation.Home;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityScreenNavigator;
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
        [SerializeField] private OverlayContainer overlayContainer;

        // BootstrapEntryPoint.StartAsync内でLifetimeScope.EnqueueParent(rootScope)を使うと、それが
        // 解除されるタイミング(ChangeScene()のawait完了)が自分自身のAwake()完了より後になるとは限らず、
        // 親の解決がタイミング依存になる。EnqueueParentの共有スタックに依存せず、常にRootLifetimeScopeを
        // 直接探すことでこのタイミング依存を無くす。
        protected override LifetimeScope FindParent() => Find<RootLifetimeScope>();

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(pageContainer);
            builder.RegisterComponent(modalContainer);
            builder.RegisterComponent(overlayContainer);
            // シーンのスコープに置き、シーンのUnloadと一緒に積んでいた画面のPresenterも破棄させる。
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
            await screenNavigator.PushPageAsync<HomePresenter>();
        }
    }
}
