using Atlas.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.Home
{
    // Push時のViewDtoは使わない画面だが、PageLifetimeScope<TViewDto>を常に使う統一ルールに
    // 合わせている(PushPageAsync<HomePage>()経由ではSetViewDtoが呼ばれず、ViewDtoはnullのまま)。
    public sealed class HomePageLifetimeScope : PageLifetimeScope<HomeViewDto>
    {
        [SerializeField] private HomePage view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
            builder.RegisterEntryPoint<HomePresenter>();
        }
    }
}
