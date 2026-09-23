using System;
using Atlas.Navigation;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;

namespace Atlas.Presentation.Battle
{
    // 投了するかどうか(bool)をPop結果として返すだけで、投了処理自体はPushした側
    // (BattlePresenter)がWaitForPopModalAsyncの結果を見て行う。
    public sealed class ForfeitConfirmPresenter : IInitializable, IDisposable
    {
        private readonly ForfeitConfirmModal view;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        public ForfeitConfirmPresenter(ForfeitConfirmModal view, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.screenNavigator = screenNavigator;
        }

        public void Initialize()
        {
            // どちらか一方を1回押した時点で閉じる(Pop中に再度押されて二重にPopしないようにする)。
            // 型引数を省略すると、boolの引数がPopModalAsync(bool playAnimation, ...)の方に解決され
            // 結果が渡らないため、<bool>を明示する。
            view.OnForfeitButtonClicked.Select(_ => true)
                .Merge(view.OnCancelButtonClicked.Select(_ => false))
                .Take(1)
                .Subscribe(forfeit => screenNavigator.PopModalAsync<bool>(forfeit).Forget())
                .AddTo(disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
