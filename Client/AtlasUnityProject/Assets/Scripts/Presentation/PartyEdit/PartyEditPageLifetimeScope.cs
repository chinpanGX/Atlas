using Atlas.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.PartyEdit
{
    public sealed class PartyEditPageLifetimeScope : PageLifetimeScope<PartyEditViewDto>
    {
        [SerializeField] private PartyEditPage view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
            builder.RegisterEntryPoint<PartyEditPresenter>();
        }
    }
}
