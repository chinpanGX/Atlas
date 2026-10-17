using System.Threading;
using Atlas.Application;
using Atlas.Presentation.GlobalHeader;
using Atlas.Presentation.Home;
using Atlas.Presentation.Title;
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
        [SerializeField] private GlobalHeaderView globalHeaderView;

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
            // 画面ごとの子スコープから、シーンに1つのヘッダーへ登録できるようにする。
            builder.RegisterComponent(globalHeaderView);
            builder.RegisterEntryPoint<GlobalHeaderPresenter>().AsSelf();
            builder.RegisterEntryPoint<HomeEntryPoint>();
        }
    }

    // サインインしていなければタイトルから、していればホームから始める(対戦から戻ったときなど)。
    internal sealed class HomeEntryPoint : IAsyncStartable
    {
        private const string TitleMessage = "Atlas";

        private readonly IScreenNavigator screenNavigator;
        private readonly IPlayerSession playerSession;

        public HomeEntryPoint(IScreenNavigator screenNavigator, IPlayerSession playerSession)
        {
            this.screenNavigator = screenNavigator;
            this.playerSession = playerSession;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            if (playerSession.IsSignedIn)
            {
                await screenNavigator.PushPageAsync<HomePresenter>();
                return;
            }

            // ホームへ進んだらタイトルには戻らないので、履歴に積まない
            await screenNavigator.PushPageAsync<TitlePresenter, TitleViewDto>(
                new TitleViewDto { Message = TitleMessage }, stack: false);
        }
    }
}
