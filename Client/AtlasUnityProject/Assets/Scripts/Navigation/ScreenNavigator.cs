using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;
using VContainer.Unity;

namespace Atlas.Navigation
{
    /// <summary>
    /// USNのPageContainer/ModalContainerをラップし、呼び出し側にUSNの型を直接触らせないための実装。
    /// Page/Modal prefab同梱の子LifetimeScopeをLifetimeScope.EnqueueParentで親付けしてBuildし、
    /// そのスコープ内でPresenterをRegisterEntryPointしておくことで、Build()の副作用として
    /// Presenterが構築される(コンストラクタでViewのObservable購読が行われる)想定。
    /// Atlas.Presentationとは別アセンブリ(Atlas.Navigation)に分離し、特定の画面/Presenterの
    /// 型を知らない汎用の画面遷移基盤として扱う。design/client-architecture.md
    /// 「INavigationService(現IScreenNavigator)」参照。
    /// </summary>
    public sealed class ScreenNavigator : IScreenNavigator
    {
        private readonly PageContainer pageContainer;
        private readonly ModalContainer modalContainer;
        private readonly LifetimeScope sceneScope;

        public ScreenNavigator(PageContainer pageContainer, ModalContainer modalContainer, LifetimeScope sceneScope)
        {
            this.pageContainer = pageContainer;
            this.modalContainer = modalContainer;
            this.sceneScope = sceneScope;
        }

        public UniTask<TPage> PushPageAsync<TPage>(bool playAnimation = true, bool stack = true,
            string resourceKey = null) where TPage : Page
        {
            return PushPageCoreAsync<TPage>(resourceKey, playAnimation, stack, static _ => { });
        }

        public UniTask<TPage> PushPageAsync<TPage, TViewDto>(TViewDto dto, bool playAnimation = true,
            bool stack = true, string resourceKey = null) where TPage : Page where TViewDto : class
        {
            return PushPageCoreAsync<TPage>(resourceKey, playAnimation, stack, lts =>
            {
                if (lts is PageLifetimeScope<TViewDto> withDto)
                {
                    withDto.SetViewDto(dto);
                }
            });
        }

        public async UniTask PopPageAsync(bool playAnimation = true, int popCount = 1)
        {
            await pageContainer.Pop(playAnimation, popCount).Task.AsUniTask();
        }

        // USNはPopの遷移完了後にPageを破棄するため、破棄を待てば続けてPush/Popしても遷移中にならない。
        public UniTask WaitForPopAsync(Page target, CancellationToken token)
        {
            return UniTask.WaitUntilCanceled(target.destroyCancellationToken, completeImmediately: true)
                .AttachExternalCancellation(token);
        }

        public UniTask<TModal> PushModalAsync<TModal>(bool playAnimation = true, string resourceKey = null)
            where TModal : Modal
        {
            return PushModalCoreAsync<TModal>(resourceKey, playAnimation, static _ => { });
        }

        public UniTask<TModal> PushModalAsync<TModal, TViewDto>(TViewDto dto, bool playAnimation = true,
            string resourceKey = null) where TModal : Modal where TViewDto : class
        {
            return PushModalCoreAsync<TModal>(resourceKey, playAnimation, lts =>
            {
                if (lts is PageLifetimeScope<TViewDto> withDto)
                {
                    withDto.SetViewDto(dto);
                }
            });
        }

        public async UniTask PopModalAsync(bool playAnimation = true, int popCount = 1)
        {
            await modalContainer.Pop(playAnimation, popCount).Task.AsUniTask();
        }

        private async UniTask<TPage> PushPageCoreAsync<TPage>(string resourceKey, bool playAnimation, bool stack,
            Action<LifetimeScope> configureScope) where TPage : Page
        {
            resourceKey ??= typeof(TPage).Name;
            TPage result = null;

            using (LifetimeScope.EnqueueParent(sceneScope))
            {
                var handle = pageContainer.Push<TPage>(resourceKey, playAnimation, stack, onLoad: x =>
                {
                    var lts = x.page.gameObject.GetComponentInChildren<LifetimeScope>();
                    configureScope(lts);
                    lts.Build();
                    result = x.page;
                });

                await handle.Task.AsUniTask();
            }

            return result;
        }

        private async UniTask<TModal> PushModalCoreAsync<TModal>(string resourceKey, bool playAnimation,
            Action<LifetimeScope> configureScope) where TModal : Modal
        {
            resourceKey ??= typeof(TModal).Name;
            TModal result = null;

            using (LifetimeScope.EnqueueParent(sceneScope))
            {
                var handle = modalContainer.Push<TModal>(resourceKey, playAnimation, onLoad: x =>
                {
                    var lts = x.modal.gameObject.GetComponentInChildren<LifetimeScope>();
                    configureScope(lts);
                    lts.Build();
                    result = x.modal;
                });

                await handle.Task.AsUniTask();
            }

            return result;
        }
    }
}
