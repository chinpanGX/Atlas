using Atlas.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.Battle
{
    public sealed class ForfeitConfirmModalLifetimeScope : PageLifetimeScope<ForfeitConfirmViewDto>
    {
        [SerializeField] private ForfeitConfirmModal view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
            builder.RegisterEntryPoint<ForfeitConfirmPresenter>();
        }
    }
}
