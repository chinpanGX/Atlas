using System.Threading;
using Atlas.Application;
using Atlas.Navigation;
using Atlas.Presentation.Battle;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;
using VContainer;
using VContainer.Unity;

namespace Atlas.DI
{
    public sealed class BattleLifetimeScope : LifetimeScope
    {
        [SerializeField] private PageContainer pageContainer;
        [SerializeField] private ModalContainer modalContainer;

        // HomeLifetimeScopeと同じ理由で、EnqueueParentの共有スタックに頼らずRootを直接親にする。
        protected override LifetimeScope FindParent() => Find<RootLifetimeScope>();

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(pageContainer);
            builder.RegisterComponent(modalContainer);
            builder.Register<IScreenNavigator, ScreenNavigator>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BattleEntryPoint>();
        }
    }

    internal sealed class BattleEntryPoint : IAsyncStartable
    {
        private readonly IScreenNavigator screenNavigator;
        private readonly BattleEntryStore battleEntryStore;

        public BattleEntryPoint(IScreenNavigator screenNavigator, BattleEntryStore battleEntryStore)
        {
            this.screenNavigator = screenNavigator;
            this.battleEntryStore = battleEntryStore;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            await screenNavigator.PushPageAsync<BattlePage, BattleViewDto>(
                new BattleViewDto { SelfPachimonIds = battleEntryStore.SelfPachimonIds });
        }
    }
}
