using Atlas.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.Battle
{
    // Presenterは持たない(表示のみで、閉じる操作はPush元のBattlePresenterが行う)。
    public sealed class OpponentSwitchingModalLifetimeScope : PageLifetimeScope<OpponentSwitchingViewDto>
    {
        [SerializeField] private OpponentSwitchingModal view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
        }
    }
}
