using System;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;

namespace Atlas.Presentation.Battle
{
    // 投了するかどうか(bool)をPop結果として返すだけで、投了処理自体はPushした側
    // (BattlePresenter)がWaitForResultAsyncの結果を見て行う。
    public sealed class ForfeitConfirmPresenter : IInitializable, IDisposable
    {
        private readonly ForfeitConfirmModal view;
        private readonly CompositeDisposable disposables = new();

        public ForfeitConfirmPresenter(ForfeitConfirmModal view)
        {
            this.view = view;
        }

        public void Initialize()
        {
            // どちらか一方を1回押した時点で閉じる(Pop中に再度押されて二重にPopしないようにする)。
            view.OnForfeitButtonClicked.Select(_ => true)
                .Merge(view.OnCancelButtonClicked.Select(_ => false))
                .Take(1)
                .Subscribe(forfeit => view.CompleteAsync(forfeit).Forget())
                .AddTo(disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
