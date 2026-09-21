using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.Title
{
    public sealed class TitlePageLifetimeScope : PageLifetimeScope<TitleViewDto>
    {
        [SerializeField] private TitlePage view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
            builder.RegisterEntryPoint<TitlePresenter>();
        }
    }
}
