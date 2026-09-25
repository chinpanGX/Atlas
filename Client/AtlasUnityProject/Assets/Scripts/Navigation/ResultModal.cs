using System.Threading;
using Cysharp.Threading.Tasks;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Navigation
{
    /// <summary>
    /// 閉じた時に結果を返すModal。Presenterは結果を決めたらCompleteAsyncで自分自身を閉じ、
    /// Pushした側はPushModalAsyncの戻り値に対してWaitForResultAsyncで結果を待つ。
    /// TResultはModalの型から推論されるため、返す側と受け取る側で型が食い違うとコンパイルエラーになる。
    /// </summary>
    public abstract class ResultModal<TResult> : Modal
    {
        private ScreenResultCompletion<TResult> completion;

        private ScreenResultCompletion<TResult> Completion => completion ??= new(() => CanceledResult);

        /// <summary>
        /// CompleteAsyncを経由せずに閉じられた場合(背景タップ、上からまとめてPop、シーンごと破棄)の結果。
        /// </summary>
        protected virtual TResult CanceledResult => default;

        public UniTask<TResult> WaitForResultAsync(CancellationToken cancellation)
        {
            return Completion.WaitAsync(this, cancellation);
        }

        /// <summary>
        /// 結果を確定して自分を閉じる。自分より上に積まれたModalがあればそれらも閉じる。
        /// 返るのはPopの遷移が終わった時点(結果の通知はその後の破棄時)。
        /// </summary>
        public UniTask CompleteAsync(TResult result, bool playAnimation = true)
        {
            return Completion.CompleteAsync(this, result, () => CloseAsync(playAnimation));
        }

        // 他の遷移と同じキューに並べ、順番が来た時点の位置から閉じる(待っている間に上へ積まれた画面も一緒に閉じる)。
        private UniTask CloseAsync(bool playAnimation)
        {
            var container = ModalContainer.Of(transform);
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

        // Modal.Identifierは既定でプレハブ名になり、コンテナ内のID(Push時に採番)とは一致しないため、
        // インスタンスで自分の位置を探す。見つからない(既に閉じている)場合は0。
        private int CountFromTop(ModalContainer container)
        {
            var ids = container.OrderedModalIds;
            for (var i = ids.Count - 1; i >= 0; i--)
            {
                if (container.Modals[ids[i]] == this)
                {
                    return ids.Count - i;
                }
            }

            return 0;
        }
    }
}
