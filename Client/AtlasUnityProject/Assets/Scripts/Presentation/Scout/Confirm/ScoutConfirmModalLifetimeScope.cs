using Atlas.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.Scout
{
    public sealed class ScoutConfirmModalLifetimeScope : PageLifetimeScope<ScoutConfirmViewDto>
    {
        [SerializeField] private ScoutConfirmModal view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
            builder.RegisterEntryPoint<ScoutConfirmPresenter>();
        }
    }
}
