using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Scout
{
    // 確定するかどうか(bool)をPop結果として返すだけで、確定の送信はPushした側(ScoutPresenter)が行う。
    // 「いいえ」以外の閉じ方(背景タップ等)も確定しない扱い。
    [AssetAddress(AddressDefinition.ScoutConfirmModal)]
    public sealed class ScoutConfirmPresenter : IScreenWithArgs<ScoutConfirmViewDto>
    {
        private readonly ScoutConfirmModal view;
        private readonly ScoutConfirmViewDto dto;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private bool confirmed;

        public ScoutConfirmPresenter(ScoutConfirmModal view, ScoutConfirmViewDto dto, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.dto = dto;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.SetMessage($"{dto.PachimonName}\nで、確定しますか？");

            // どちらか一方を1回押した時点で閉じる(Pop中に再度押されて二重にPopしないようにする)。
            view.OnYesButtonClicked.Select(_ => true)
                .Merge(view.OnNoButtonClicked.Select(_ => false))
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
