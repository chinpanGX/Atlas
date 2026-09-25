using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Atlas.Navigation
{
    /// <summary>
    /// 1つのコンテナ(PageContainer/ModalContainer)への遷移を、要求された順に1つずつ実行する。
    /// USNは遷移中のPush/Popを例外で拒否するため、別々の場所から来た遷移(開くアニメーション中の
    /// エラーダイアログ、閉じる途中の結果画面など)が重なると後の方が失敗する。これを順番待ちにする。
    /// ScreenNavigatorとResultModal/ResultPage(自分を閉じるPop)の両方が使うため、コンテナにひも付けて持つ。
    /// ユーザーの連打は対象外(遷移中の入力はUSNがCanvasGroupで止める。遷移前の連打はPresenter側で捨てる)。
    /// </summary>
    internal sealed class TransitionQueue
    {
        private static readonly ConditionalWeakTable<Component, TransitionQueue> Queues = new();

        private UniTask tail = UniTask.CompletedTask;

        public static TransitionQueue For(Component container) => Queues.GetValue(container, _ => new TransitionQueue());

        /// <summary>
        /// 前の遷移が終わってから(成功・失敗を問わず)transitionを実行する。
        /// 順番が来た時点でコンテナが破棄されていれば(シーンの切り替え等)、実行せずにキャンセル扱いにする。
        /// </summary>
        public async UniTask<T> EnqueueAsync<T>(Component container, Func<UniTask<T>> transition)
        {
            var previous = tail;
            var done = new UniTaskCompletionSource();
            tail = done.Task;
            try
            {
                await previous;
                if (container == null)
                {
                    throw new OperationCanceledException("The screen container was destroyed before the transition started.");
                }

                return await transition();
            }
            finally
            {
                done.TrySetResult();
            }
        }

        public UniTask EnqueueAsync(Component container, Func<UniTask> transition)
        {
            return EnqueueAsync(container, async () =>
            {
                await transition();
                return AsyncUnit.Default;
            });
        }
    }
}
