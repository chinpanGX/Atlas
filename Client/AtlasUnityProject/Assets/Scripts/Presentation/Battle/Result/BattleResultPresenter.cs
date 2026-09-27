using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Battle
{
    [AssetAddress(AddressDefinition.BattleResultModal)]
    public sealed class BattleResultPresenter : IScreenWithArgs<BattleResultViewDto>
    {
        private readonly BattleResultModal view;
        private readonly BattleResultViewDto initialDto;
        private readonly ISceneNavigator sceneNavigator;
        private readonly CompositeDisposable disposables = new();

        public BattleResultPresenter(BattleResultModal view, BattleResultViewDto initialDto, ISceneNavigator sceneNavigator)
        {
            this.view = view;
            this.initialDto = initialDto;
            this.sceneNavigator = sceneNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.Refresh(initialDto);

            // ChangeSceneAsyncの中でBattleシーン(=このModal自身)がUnloadされるため、
            // 連打で2回目の遷移が走らないよう最初の1回だけ受け付ける。
            view.OnHomeButtonClicked
                .Take(1)
                .Subscribe(_ => sceneNavigator.ChangeSceneAsync(AddressDefinition.Home).Forget())
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
