using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Title
{
    [AssetAddress(AddressDefinition.TitlePage)]
    public sealed class TitlePresenter : IScreenWithArgs<TitleViewDto>
    {
        private readonly TitlePage view;
        private readonly TitleViewDto initialDto;
        private readonly CompositeDisposable disposables = new();

        public TitlePresenter(TitlePage view, TitleViewDto initialDto)
        {
            this.view = view;
            this.initialDto = initialDto;
        }

        public UniTask InitializeAsync()
        {
            view.Refresh(initialDto);

            view.OnStartButtonClicked
                .Subscribe(_ => view.Refresh(new TitleViewDto { Message = "Started!" }))
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
