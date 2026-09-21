using System;
using R3;
using VContainer.Unity;

namespace Atlas.Presentation.Title
{
    public sealed class TitlePresenter : IInitializable, IDisposable
    {
        private readonly TitlePage view;
        private readonly TitleViewDto initialDto;
        private readonly CompositeDisposable disposables = new();

        public TitlePresenter(TitlePage view, TitleViewDto initialDto)
        {
            this.view = view;
            this.initialDto = initialDto;
        }

        public void Initialize()
        {
            view.Refresh(initialDto);

            view.OnStartButtonClicked
                .Subscribe(_ => view.Refresh(new TitleViewDto { Message = "Started!" }))
                .AddTo(disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
