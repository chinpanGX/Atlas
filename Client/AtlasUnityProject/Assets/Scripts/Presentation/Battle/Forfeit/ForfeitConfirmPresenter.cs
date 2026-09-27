using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Battle
{
    // 投了するかどうか(bool)をPop結果として返すだけで、投了処理自体はPushした側
    // (BattlePresenter)がWaitForPopAsyncの結果を見て行う。キャンセル以外の閉じ方も投了しない扱い。
    [AssetAddress(AddressDefinition.ForfeitConfirmModal)]
    public sealed class ForfeitConfirmPresenter : IPresenter
    {
        private readonly ForfeitConfirmModal view;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private bool forfeit;

        public ForfeitConfirmPresenter(ForfeitConfirmModal view, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            // どちらか一方を1回押した時点で閉じる(Pop中に再度押されて二重にPopしないようにする)。
            view.OnForfeitButtonClicked.Select(_ => true)
                .Merge(view.OnCancelButtonClicked.Select(_ => false))
                .Take(1)
                .Subscribe(value =>
                {
                    forfeit = value;
                    screenNavigator.PopModalAsync(this).Forget();
                })
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        public UniTask<object> CompleteAsync()
        {
            return UniTask.FromResult<object>(forfeit);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
