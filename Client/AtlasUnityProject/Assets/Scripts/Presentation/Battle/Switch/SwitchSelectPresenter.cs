using System;
using Atlas.Navigation;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;

namespace Atlas.Presentation.Battle
{
    // 選ばれた交代先(またはやめた)をPop結果として返すだけで、交代の送信はPush元
    // (BattlePresenter)がWaitForPopModalAsyncの結果を見て行う(ForfeitConfirmPresenterと同じ方針)。
    public sealed class SwitchSelectPresenter : IInitializable, IDisposable
    {
        private readonly SwitchSelectModal view;
        private readonly SwitchSelectViewDto initialDto;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        public SwitchSelectPresenter(SwitchSelectModal view, SwitchSelectViewDto initialDto, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.initialDto = initialDto;
            this.screenNavigator = screenNavigator;
        }

        public void Initialize()
        {
            view.Refresh(initialDto);

            // どれか1回押した時点で閉じる(Pop中に再度押されて二重にPopしないようにする)。
            view.OnCandidateClicked.Select(SwitchSelectResult.Selected)
                .Merge(view.OnCancelButtonClicked.Select(_ => SwitchSelectResult.Canceled))
                .Take(1)
                .Subscribe(result => screenNavigator.PopModalAsync<SwitchSelectResult>(result).Forget())
                .AddTo(disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
