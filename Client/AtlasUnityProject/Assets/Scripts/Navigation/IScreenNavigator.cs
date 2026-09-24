using System.Threading;
using Cysharp.Threading.Tasks;
using UnityScreenNavigator.Runtime.Core.Modal;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Navigation
{
    /// <summary>
    /// 結果を返す画面はResultModal/ResultPageを継承し、Push後にWaitForResultAsyncで待つ
    /// (結果の返却・自分を閉じる処理は画面側のCompleteAsync)。
    /// </summary>
    public interface IScreenNavigator
    {
        UniTask<TPage> PushPageAsync<TPage>(bool playAnimation = true, bool stack = true, string resourceKey = null)
            where TPage : Page;

        UniTask<TPage> PushPageAsync<TPage, TViewDto>(TViewDto dto, bool playAnimation = true,
            bool stack = true, string resourceKey = null) where TPage : Page where TViewDto : class;

        UniTask PopPageAsync(bool playAnimation = true, int popCount = 1);

        /// <summary>
        /// 結果を返さないPageが閉じる(破棄される)まで待つ。
        /// </summary>
        UniTask WaitForPopAsync(Page target, CancellationToken token);

        UniTask<TModal> PushModalAsync<TModal>(bool playAnimation = true, string resourceKey = null)
            where TModal : Modal;

        UniTask<TModal> PushModalAsync<TModal, TViewDto>(TViewDto dto, bool playAnimation = true,
            string resourceKey = null) where TModal : Modal where TViewDto : class;

        UniTask PopModalAsync(bool playAnimation = true, int popCount = 1);
    }
}
