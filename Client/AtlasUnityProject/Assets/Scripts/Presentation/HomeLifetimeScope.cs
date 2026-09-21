using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation
{
    /// <summary>
    /// Homeシーンのスコープ。design/client-architecture.md「シーン構成」参照。
    /// RootLifetimeScopeの子として、シーンロード時にLifetimeScope.EnqueueParent経由で構築される想定。
    /// </summary>
    public sealed class HomeLifetimeScope : LifetimeScope
    {
        [SerializeField] private PageContainer pageContainer;
        [SerializeField] private ModalContainer modalContainer;

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
            await screenNavigator.PushPageAsync<Home.HomePage>();
        }
    }
}
