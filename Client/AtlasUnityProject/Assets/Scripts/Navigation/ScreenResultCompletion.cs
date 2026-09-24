using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Atlas.Navigation
{
    /// <summary>
    /// ResultModal/ResultPageの結果の受け渡し。結果の通知は画面の破棄(destroyCancellationToken)に合わせて行う。
    /// USNはPopの遷移完了後に画面を破棄するため、通知を受けた側が続けてPush/Popしても
    /// "screen is already in transition"にならない。また、CompleteAsyncを経由しない閉じ方
    /// (背景タップで閉じる、PopModalAsyncで上からまとめて閉じる、シーンごと破棄される)でも
    /// 待機が終わらなくなることがなく、その場合はCanceledResultを返す。
    /// </summary>
    internal sealed class ScreenResultCompletion<TResult>
    {
        private readonly UniTaskCompletionSource<TResult> completion = new();
        private readonly Func<TResult> canceledResult;

        private bool destroyHooked;
        private bool hasResult;
        private TResult result;
        private UniTask closeTask;

        public ScreenResultCompletion(Func<TResult> canceledResult)
        {
            this.canceledResult = canceledResult;
        }

        public UniTask<TResult> WaitAsync(MonoBehaviour owner, CancellationToken cancellation)
        {
            HookDestroy(owner);
            return completion.Task.AttachExternalCancellation(cancellation);
        }

        // 2回目以降(ボタンの連打、画面側と呼び出し側の両方からの完了など)は最初の結果を優先し、
        // 進行中の閉じる処理の完了だけを待たせる。
        public UniTask CompleteAsync(MonoBehaviour owner, TResult value, Func<UniTask> close)
        {
            HookDestroy(owner);
            if (hasResult)
            {
                return closeTask;
            }

            hasResult = true;
            result = value;
            closeTask = close().Preserve();
            return closeTask;
        }

        private void HookDestroy(MonoBehaviour owner)
        {
            if (destroyHooked)
            {
                return;
            }

            destroyHooked = true;
            owner.destroyCancellationToken.Register(() => completion.TrySetResult(hasResult ? result : canceledResult()));
        }
    }
}
