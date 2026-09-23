using Atlas.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation.Party
{
    // Push時のViewDtoは使わない画面(PushPageAsync<PartyPage>()で開き、表示データはPresenterが
    // 自分で組み立てる)。PageLifetimeScope<TViewDto>を常に使う統一ルールに合わせてPartyDtoを指定している。
    public sealed class PartyPageLifetimeScope : PageLifetimeScope<PartyDto>
    {
        [SerializeField] private PartyPage view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            RegisterViewDto(builder);
            builder.RegisterEntryPoint<PartyPresenter>();
        }
    }
}
