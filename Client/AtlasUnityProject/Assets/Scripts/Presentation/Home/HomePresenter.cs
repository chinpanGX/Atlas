using System;
using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Presentation.Party;
using Atlas.Presentation.Scout;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityScreenNavigator;

namespace Atlas.Presentation.Home
{
    [AssetAddress(AddressDefinition.HomePage)]
    public sealed class HomePresenter : IPresenter
    {
        // itemsマスタのitem_id=1(ジェム)。
        private const int GemItemId = 1;

        private readonly HomePage view;
        private readonly IPlayerAccountService playerService;
        private readonly IItemService itemService;
        private readonly IDebugConnectService debugConnectService;
        private readonly IScreenNavigator screenNavigator;
        private readonly ISceneNavigator sceneNavigator;
        private readonly BattleEntryStore battleEntryStore;
        private readonly IBattleMatchMaker battleMatchMaker;
        private readonly CompositeDisposable disposables = new();

        public HomePresenter(HomePage view, IPlayerAccountService playerService, IItemService itemService,
            IDebugConnectService debugConnectService, IScreenNavigator screenNavigator, ISceneNavigator sceneNavigator,
            BattleEntryStore battleEntryStore, IBattleMatchMaker battleMatchMaker)
        {
            this.view = view;
            this.playerService = playerService;
            this.itemService = itemService;
            this.debugConnectService = debugConnectService;
            this.screenNavigator = screenNavigator;
            this.sceneNavigator = sceneNavigator;
            this.battleEntryStore = battleEntryStore;
            this.battleMatchMaker = battleMatchMaker;
        }

        // HomeはPush時のViewDtoを持たず、自分でIPlayerAccountServiceから初期データを取得する。
        public UniTask InitializeAsync()
        {
            view.Refresh(CreateDto());

            // 動作確認用。連打でも1回分ずつ送るよう、実行中の押下は待たせる(捨てない)。
            view.OnGrantGemsButtonClicked
                .SubscribeAwait(async (_, _) =>
                {
                    await debugConnectService.GrantGemsAsync();
                    view.Refresh(CreateDto());
                })
                .AddTo(disposables);
            // Push完了までの連打で同じPageを重ねて積まないよう、実行中の押下は捨てる。
            view.OnScoutButtonClicked
                .SubscribeAwait(async (_, _) => await screenNavigator.PushPageAsync<ScoutPresenter>(), AwaitOperation.Drop)
                .AddTo(disposables);
            view.OnPartyButtonClicked
                .SubscribeAwait(async (_, _) => await screenNavigator.PushPageAsync<PartyPresenter>(), AwaitOperation.Drop)
                .AddTo(disposables);
            // マッチング待ち〜シーン切り替えの間の連打は捨てる。キャンセルした場合は再び押せる。
            view.OnBattleButtonClicked
                .SubscribeAwait(async (_, ct) => await FindMatchAndStartBattleAsync(ct), AwaitOperation.Drop)
                .AddTo(disposables);
            // 育成画面は未実装のため、現時点ではログのみ。
            view.OnTrainingButtonClicked.Subscribe(_ => Debug.Log("[Home] Training button clicked (not implemented yet)")).AddTo(disposables);
            return UniTask.CompletedTask;
        }

        // スカウトでジェムが減るため、上に積んだ画面から戻ってきたらHomeの表示を取り直す。
        public UniTask WillPopEnterAsync()
        {
            view.Refresh(CreateDto());
            return UniTask.CompletedTask;
        }

        // マッチング待ちModalを出して対戦相手を探し、成立したらBattleシーンへ切り替える。キャンセルボタンを
        // 購読するためにPushの完了を待ってからマッチングを始め、Modalが閉じてからシーンを切り替える。
        // Battleは別シーンのため、マッチング結果はPush時のViewDtoではなくBattleEntryStore経由で受け渡す。
        private async UniTask FindMatchAndStartBattleAsync(CancellationToken cancellation)
        {
            var matchmaking = await screenNavigator.PushModalAsync<MatchmakingPresenter>();

            BattleMatch match = null;
            using (var matchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (matchmaking.OnCancelButtonClicked.Take(1).Subscribe(_ => matchCancellation.Cancel()))
            {
                try
                {
                    match = await battleMatchMaker.FindMatchAsync(matchCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }

            await screenNavigator.PopModalAsync(matchmaking);
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
            var gems = itemService.GetAmount(GemItemId);
            return new HomeViewDto { PlayerId = player.PlayerId, Gems = gems };
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
