using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Training
{
    // ショップへ移るかどうか(bool)をPop結果として返すだけで、ショップを開くのはPushした側が行う。
    // 「やめる」以外の閉じ方(背景タップ等)もショップへ移らない扱い。
    [AssetAddress(AddressDefinition.TicketShortageModal)]
    public sealed class TicketShortagePresenter : IScreenWithArgs<TicketShortageViewDto>
    {
        private readonly TicketShortageModal view;
        private readonly TicketShortageViewDto dto;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private bool goesToShop;

        public TicketShortagePresenter(TicketShortageModal view, TicketShortageViewDto dto, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.dto = dto;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.Refresh(dto);

            view.OnShopButtonClicked.Select(_ => true)
                .Merge(view.OnCancelButtonClicked.Select(_ => false))
                .Take(1)
                .Subscribe(value =>
                {
                    goesToShop = value;
                    screenNavigator.PopModalAsync(this).Forget();
                })
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        public UniTask<object> CompleteAsync()
        {
            return UniTask.FromResult<object>(goesToShop);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
