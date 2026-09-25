using System.Threading;
using Cysharp.Threading.Tasks;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Navigation
{
    /// <summary>
    /// 閉じた時に結果を返すPage。使い方・結果の通知タイミングはResultModalと同じ。
    /// stack=falseでPushした場合、次のPageのPushで破棄された時点でCanceledResultが返る。
    /// </summary>
    public abstract class ResultPage<TResult> : Page
    {
        private ScreenResultCompletion<TResult> completion;

        private ScreenResultCompletion<TResult> Completion => completion ??= new(() => CanceledResult);

        protected virtual TResult CanceledResult => default;

        public UniTask<TResult> WaitForResultAsync(CancellationToken cancellation)
        {
            return Completion.WaitAsync(this, cancellation);
        }

        /// <summary>
        /// 結果を確定して自分を閉じる。自分より上に積まれたPageがあればそれらも閉じる。
        /// </summary>
        public UniTask CompleteAsync(TResult result, bool playAnimation = true)
        {
            return Completion.CompleteAsync(this, result, () => CloseAsync(playAnimation));
        }

        // 他の遷移と同じキューに並べ、順番が来た時点の位置から閉じる(待っている間に上へ積まれた画面も一緒に閉じる)。
        private UniTask CloseAsync(bool playAnimation)
        {
            var container = PageContainer.Of(transform);
            return TransitionQueue.For(container).EnqueueAsync(container, async () =>
            {
                // 順番待ちの間に別の経路(上からまとめてPop等)で閉じられていれば何もしない。
                if (this == null)
                {
                    return;
                }

                var popCount = CountFromTop(container);
                if (popCount == 0)
                {
                    return;
                }

                await container.Pop(playAnimation, popCount).Task.AsUniTask();
            });
        }

        private int CountFromTop(PageContainer container)
        {
            var ids = container.OrderedPagesIds;
            for (var i = ids.Count - 1; i >= 0; i--)
            {
                if (container.Pages[ids[i]] == this)
                {
                    return ids.Count - i;
                }
            }

            return 0;
        }
    }
}
