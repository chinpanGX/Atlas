using Atlas.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.Battle
{
    public sealed class SwitchSelectModalLifetimeScope : PageLifetimeScope<SwitchSelectViewDto>
    {
        [SerializeField] private SwitchSelectModal view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
            builder.RegisterEntryPoint<SwitchSelectPresenter>();
        }
    }
}
