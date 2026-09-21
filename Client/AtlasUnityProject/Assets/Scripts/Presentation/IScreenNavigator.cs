using System.Threading;
using Cysharp.Threading.Tasks;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation
{
    public interface IScreenNavigator
    {
        UniTask<TPage> PushPageAsync<TPage>(bool playAnimation = true, bool stack = true, string resourceKey = null)
            where TPage : Page;

        UniTask<TPage> PushPageAsync<TPage, TViewDto>(TViewDto dto, bool playAnimation = true,
            bool stack = true, string resourceKey = null) where TPage : Page where TViewDto : class;

        UniTask PopPageAsync(bool playAnimation = true, int popCount = 1);
        UniTask PopPageAsync<TResult>(TResult result, bool playAnimation = true);
        UniTask<TResult> WaitForPopAsync<TResult>(Page target, CancellationToken token);

        UniTask<TModal> PushModalAsync<TModal>(bool playAnimation = true, string resourceKey = null)
            where TModal : Modal;

        UniTask<TModal> PushModalAsync<TModal, TViewDto>(TViewDto dto, bool playAnimation = true,
            string resourceKey = null) where TModal : Modal where TViewDto : class;

        UniTask PopModalAsync(bool playAnimation = true, int popCount = 1);
        UniTask PopModalAsync<TResult>(TResult result, bool playAnimation = true);
        UniTask<TResult> WaitForPopModalAsync<TResult>(Modal target, CancellationToken token);
    }
}
