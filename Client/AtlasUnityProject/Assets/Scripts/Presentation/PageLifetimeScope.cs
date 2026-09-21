using VContainer;
using VContainer.Unity;

namespace Atlas.Presentation
{
    /// <summary>
    /// Presenterを持つ画面のPage/Modal prefabが同一GameObjectに乗せる、子LifetimeScopeの基底クラス。
    /// Push時にViewDtoを渡さない画面もこれを継承し、ViewDtoはnullのまま使わなければよい。
    /// IScreenNavigator実装がBuild()前にSetViewDtoを呼ぶ(自動Buildは無効化しておくこと)。
    /// </summary>
    public abstract class PageLifetimeScope<TViewDto> : LifetimeScope
        where TViewDto : class
    {
        protected TViewDto ViewDto { get; private set; }

        public void SetViewDto(TViewDto dto)
        {
            ViewDto = dto;
        }

        // VContainerのRegisterInstanceはnullを渡すとNullReferenceExceptionになるため、
        // ViewDtoを使わない画面(SetViewDtoが一度も呼ばれずnullのまま)向けにガードする。
        // Configure内ではbuilder.RegisterInstance(ViewDto)ではなくこちらを使うこと。
        protected void RegisterViewDto(IContainerBuilder builder)
        {
            if (ViewDto != null)
            {
                builder.RegisterInstance(ViewDto);
            }
        }
    }
}
