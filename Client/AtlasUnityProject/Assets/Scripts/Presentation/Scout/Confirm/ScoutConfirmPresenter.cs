using System;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;

namespace Atlas.Presentation.Scout
{
    // 確定するかどうか(bool)をPop結果として返すだけで、確定の送信はPushした側(ScoutPresenter)が行う。
    public sealed class ScoutConfirmPresenter : IInitializable, IDisposable
    {
        private readonly ScoutConfirmModal view;
        private readonly ScoutConfirmViewDto dto;
        private readonly CompositeDisposable disposables = new();

        public ScoutConfirmPresenter(ScoutConfirmModal view, ScoutConfirmViewDto dto)
        {
            this.view = view;
            this.dto = dto;
        }

        public void Initialize()
        {
            view.SetMessage($"{dto.PachimonName}\nで、確定しますか？");

            view.OnYesButtonClicked.Select(_ => true)
                .Merge(view.OnNoButtonClicked.Select(_ => false))
                .Take(1)
                .Subscribe(confirmed => view.CompleteAsync(confirmed).Forget())
                .AddTo(disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
