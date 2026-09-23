using System;
using System.Collections.Generic;
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

        private readonly Dictionary<Page, UniTaskCompletionSource<object>> pageCompletionSources = new();
        private readonly Dictionary<Modal, UniTaskCompletionSource<object>> modalCompletionSources = new();

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
            var pending = CollectPendingPageSources(popCount);
            var handle = pageContainer.Pop(playAnimation, popCount);
            await handle.Task.AsUniTask();
            SetResults(pending, default(object));
        }

        public async UniTask PopPageAsync<TResult>(TResult result, bool playAnimation = true)
        {
            var pending = CollectPendingPageSources(1);
            var handle = pageContainer.Pop(playAnimation, 1);
            await handle.Task.AsUniTask();
            SetResults(pending, result);
        }

        public UniTask<TResult> WaitForPopAsync<TResult>(Page target, CancellationToken token)
        {
            if (pageCompletionSources.ContainsKey(target))
            {
                throw new InvalidOperationException(
                    $"The page '{target}' is already being awaited for pop.");
            }

            var completionSource = new UniTaskCompletionSource<object>();
            token.Register(static state => ((UniTaskCompletionSource<object>)state).TrySetCanceled(), completionSource);
            pageCompletionSources.Add(target, completionSource);
            return AwaitResultAsync<TResult, Page>(completionSource, target, pageCompletionSources);
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
            var pending = CollectPendingModalSources(popCount);
            var handle = modalContainer.Pop(playAnimation, popCount);
            await handle.Task.AsUniTask();
            SetResults(pending, default(object));
        }

        public async UniTask PopModalAsync<TResult>(TResult result, bool playAnimation = true)
        {
            var pending = CollectPendingModalSources(1);
            var handle = modalContainer.Pop(playAnimation, 1);
            await handle.Task.AsUniTask();
            SetResults(pending, result);
        }

        public UniTask<TResult> WaitForPopModalAsync<TResult>(Modal target, CancellationToken token)
        {
            if (modalCompletionSources.ContainsKey(target))
            {
                throw new InvalidOperationException(
                    $"The modal '{target}' is already being awaited for pop.");
            }

            var completionSource = new UniTaskCompletionSource<object>();
            token.Register(static state => ((UniTaskCompletionSource<object>)state).TrySetCanceled(), completionSource);
            modalCompletionSources.Add(target, completionSource);
            return AwaitResultAsync<TResult, Modal>(completionSource, target, modalCompletionSources);
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

        private static async UniTask<TResult> AwaitResultAsync<TResult, TKey>(UniTaskCompletionSource<object> completionSource,
            TKey target, Dictionary<TKey, UniTaskCompletionSource<object>> sources)
        {
            var result = await completionSource.Task;
            sources.Remove(target);
            return (TResult)result;
        }

        // Pop対象の待機者はPop前(対象がまだコンテナに居る間)に集めておき、結果の通知は
        // Popの遷移アニメーション完了後に行う(SetResults)。完了前に通知すると、待機側が
        // 続けて別のPage/ModalをPushした時にUSNが"screen is already in transition"で拒否する。
        private List<UniTaskCompletionSource<object>> CollectPendingPageSources(int popCount)
        {
            var pending = new List<UniTaskCompletionSource<object>>();
            var orderedIds = pageContainer.OrderedPagesIds;
            for (var i = orderedIds.Count - 1; i >= 0 && i >= orderedIds.Count - popCount; i--)
            {
                var page = pageContainer.Pages[orderedIds[i]];
                if (pageCompletionSources.TryGetValue(page, out var source))
                {
                    pending.Add(source);
                }
            }

            return pending;
        }

        private List<UniTaskCompletionSource<object>> CollectPendingModalSources(int popCount)
        {
            var pending = new List<UniTaskCompletionSource<object>>();
            var orderedIds = modalContainer.OrderedModalIds;
            for (var i = orderedIds.Count - 1; i >= 0 && i >= orderedIds.Count - popCount; i--)
            {
                var modal = modalContainer.Modals[orderedIds[i]];
                if (modalCompletionSources.TryGetValue(modal, out var source))
                {
                    pending.Add(source);
                }
            }

            return pending;
        }

        private static void SetResults(List<UniTaskCompletionSource<object>> pending, object result)
        {
            foreach (var source in pending)
            {
                source.TrySetResult(result);
            }
        }
    }
}
