using Atlas.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.Home
{
    // Presenterは持たない(操作はPush元のHomePresenterがMatchmakingModalを直接購読する)。
    public sealed class MatchmakingModalLifetimeScope : PageLifetimeScope<MatchmakingViewDto>
    {
        [SerializeField] private MatchmakingModal view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
        }
    }
}
