using System;
using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Navigation;
using Atlas.Presentation.Party;
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
        private readonly IBattleMatchmaker battleMatchmaker;
        private readonly CompositeDisposable disposables = new();

        public HomePresenter(HomePage view, IPlayerAccountService playerService, IItemFetchService itemFetchService,
            IScreenNavigator screenNavigator, ISceneNavigator sceneNavigator, BattleEntryStore battleEntryStore,
            IBattleMatchmaker battleMatchmaker)
        {
            this.view = view;
            this.playerService = playerService;
            this.itemFetchService = itemFetchService;
            this.screenNavigator = screenNavigator;
            this.sceneNavigator = sceneNavigator;
            this.battleEntryStore = battleEntryStore;
            this.battleMatchmaker = battleMatchmaker;
        }

        // HomeはPush時のViewDtoを持たず、自分でIPlayerAccountServiceから初期データを取得する。
        // 非同期の初期化が必要なためIInitializableではなくIAsyncStartableを使う
        // (同期で済む場合はTitlePresenterのようにIInitializableでよい)
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            var dto = CreateDto();
            view.Refresh(dto);

            // Scout/チャットの各画面は未実装のため、現時点ではログのみ。
            // 各画面を実装するタイミングでIScreenNavigator.PushPageAsyncに置き換える
            view.OnScoutButtonClicked.Subscribe(_ => Debug.Log("[Home] Scout button clicked (not implemented yet)")).AddTo(disposables);
            // Push完了までの連打で同じPageを重ねて積まないよう、実行中の押下は捨てる。
            view.OnPartyButtonClicked
                .SubscribeAwait(async (_, _) => await screenNavigator.PushPageAsync<PartyPage>(), AwaitOperation.Drop)
                .AddTo(disposables);
            // マッチング待ち〜シーン切り替えの間の連打は捨てる。キャンセルした場合は再び押せる。
            view.OnBattleButtonClicked
                .SubscribeAwait(async (_, ct) => await FindMatchAndStartBattleAsync(ct), AwaitOperation.Drop)
                .AddTo(disposables);
            view.OnChatButtonClicked.Subscribe(_ => Debug.Log("[Home] Chat button clicked (not implemented yet)")).AddTo(disposables);
            await UniTask.CompletedTask;
        }

        // マッチング待ちModalを出して対戦相手を探し、成立したらBattleシーンへ切り替える。USNは遷移中の
        // Push/Popを拒否するため、Pushの完了を待ってからマッチングを始め、Popの完了を待ってから切り替える
        // (Mockのマッチングは待たずに成立するため、この順序を守らないとPush中のPopになる)。
        // Battleは別シーンのため、マッチング結果はPush時のViewDtoではなくBattleEntryStore経由で受け渡す。
        private async UniTask FindMatchAndStartBattleAsync(CancellationToken cancellation)
        {
            var modal = await screenNavigator.PushModalAsync<MatchmakingModal>();

            BattleMatch match = null;
            using (var matchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (modal.OnCancelButtonClicked.Take(1).Subscribe(_ => matchCancellation.Cancel()))
            {
                try
                {
                    match = await battleMatchmaker.FindMatchAsync(matchCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    // パーティが空(400)等。エラーModalの仕組み(残タスク)ができるまではログのみ。
                    Debug.LogError($"[Home] マッチングに失敗しました: {e.Message}");
                }
            }

            await screenNavigator.PopModalAsync();
            if (match is null)
            {
                return;
            }

            battleEntryStore.Set(match);
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
