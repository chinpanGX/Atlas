using System;
using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Navigation;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Atlas.Presentation.Home
{
    public sealed class HomePresenter : IAsyncStartable, IDisposable
    {
        // itemsマスタのitem_id=1(ジェム)。
        private const int GemItemId = 1;

        private readonly HomePage view;
        private readonly IPlayerAccountService playerService;
        private readonly IItemFetchService itemFetchService;
        private readonly IScreenNavigator screenNavigator;
        private readonly ISceneNavigator sceneNavigator;
        private readonly BattleEntryStore battleEntryStore;
        private readonly CompositeDisposable disposables = new();

        public HomePresenter(HomePage view, IPlayerAccountService playerService, IItemFetchService itemFetchService,
            IScreenNavigator screenNavigator, ISceneNavigator sceneNavigator, BattleEntryStore battleEntryStore)
        {
            this.view = view;
            this.playerService = playerService;
            this.itemFetchService = itemFetchService;
            this.screenNavigator = screenNavigator;
            this.sceneNavigator = sceneNavigator;
            this.battleEntryStore = battleEntryStore;
        }

        // HomeはPush時のViewDtoを持たず、自分でIPlayerAccountServiceから初期データを取得する。
        // 非同期の初期化が必要なためIInitializableではなくIAsyncStartableを使う
        // (同期で済む場合はTitlePresenterのようにIInitializableでよい)
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            var dto = CreateDto();
            view.Refresh(dto);

            // Scout/パーティ編成/チャットの各画面は未実装のため、現時点ではログのみ。
            // 各画面を実装するタイミングでIScreenNavigator.PushPageAsyncに置き換える
            view.OnScoutButtonClicked.Subscribe(_ => Debug.Log("[Home] Scout button clicked (not implemented yet)")).AddTo(disposables);
            view.OnPartyButtonClicked.Subscribe(_ => Debug.Log("[Home] Party button clicked (not implemented yet)")).AddTo(disposables);
            // ChangeSceneAsyncの中でHomeシーン(=このPage自身)がUnloadされるため、連打で
            // 2回目の遷移が走らないよう最初の1回だけ受け付ける。
            view.OnBattleButtonClicked.Take(1).Subscribe(_ => OnBattleButtonClicked().Forget()).AddTo(disposables);
            view.OnChatButtonClicked.Subscribe(_ => Debug.Log("[Home] Chat button clicked (not implemented yet)")).AddTo(disposables);
            await UniTask.CompletedTask;
        }

        // player_pachimonが未実装のため、選出3体はマスターデータのPachimonIdを暫定的に固定値で
        // 渡す(design/battle.md「Stage 1」参照。実装時にパーティ編成結果へ置き換える)。
        // Battleは別シーンのため、Push時のViewDtoではなくBattleEntryStore経由で受け渡す。
        private async UniTaskVoid OnBattleButtonClicked()
        {
            battleEntryStore.Set(new[] { "1001", "1002", "1003" });
            await sceneNavigator.ChangeSceneAsync(AddressDefinition.Battle);
        }

        private HomeViewDto CreateDto()
        {
            var player = playerService.Get();
            var gems = itemFetchService.GetAmount(GemItemId);
            return new HomeViewDto { PlayerId = player.PlayerId, Gems = gems };
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
