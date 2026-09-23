using System;
using Atlas.Application.Address;
using Atlas.Navigation;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;

namespace Atlas.Presentation.Battle
{
    public sealed class BattleResultPresenter : IInitializable, IDisposable
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

        public void Initialize()
        {
            view.Refresh(initialDto);

            // ChangeSceneAsyncの中でBattleシーン(=このModal自身)がUnloadされるため、
            // 連打で2回目の遷移が走らないよう最初の1回だけ受け付ける。
            view.OnHomeButtonClicked
                .Take(1)
                .Subscribe(_ => sceneNavigator.ChangeSceneAsync(AddressDefinition.Home).Forget())
                .AddTo(disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
