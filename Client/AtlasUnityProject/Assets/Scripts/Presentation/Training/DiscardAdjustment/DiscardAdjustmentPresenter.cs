using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Training
{
    // 調整を捨てるかどうか(bool)をPop結果として返すだけで、捨てるのはPushした側が行う。
    // 「やめる」以外の閉じ方(背景タップ等)も捨てない扱い。
    [AssetAddress(AddressDefinition.DiscardAdjustmentModal)]
    public sealed class DiscardAdjustmentPresenter : IPresenter
    {
        private readonly DiscardAdjustmentModal view;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private bool discards;

        public DiscardAdjustmentPresenter(DiscardAdjustmentModal view, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.OnDiscardButtonClicked.Select(_ => true)
                .Merge(view.OnCancelButtonClicked.Select(_ => false))
                .Take(1)
                .Subscribe(value =>
                {
                    discards = value;
                    screenNavigator.PopModalAsync(this).Forget();
                })
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        public UniTask<object> CompleteAsync()
        {
            return UniTask.FromResult<object>(discards);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
