using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Shop
{
    // 購入するかどうか(bool)をPop結果として返すだけで、購入の送信はPushした側(ShopPresenter)が行う。
    // 「やめる」以外の閉じ方(背景タップ等)も購入しない扱い。
    [AssetAddress(AddressDefinition.ShopConfirmModal)]
    public sealed class ShopConfirmPresenter : IScreenWithArgs<ShopConfirmViewDto>
    {
        private readonly ShopConfirmModal view;
        private readonly ShopConfirmViewDto dto;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private bool confirmed;

        public ShopConfirmPresenter(ShopConfirmModal view, ShopConfirmViewDto dto, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.dto = dto;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.Refresh(dto);

            // どちらか一方を1回押した時点で閉じる(Pop中に再度押されて二重にPopしないようにする)。
            view.OnPurchaseButtonClicked.Select(_ => true)
                .Merge(view.OnCancelButtonClicked.Select(_ => false))
                .Take(1)
                .Subscribe(value =>
                {
                    confirmed = value;
                    screenNavigator.PopModalAsync(this).Forget();
                })
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        public UniTask<object> CompleteAsync()
        {
            return UniTask.FromResult<object>(confirmed);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
