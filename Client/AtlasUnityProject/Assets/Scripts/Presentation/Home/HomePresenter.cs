using System;
using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Presentation.GlobalHeader;
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
        private readonly HomePage view;
        private readonly IPlayerAccountService playerService;
        private readonly IScreenNavigator screenNavigator;
        private readonly ISceneNavigator sceneNavigator;
        private readonly BattleEntryStore battleEntryStore;
        private readonly IBattleMatchMaker battleMatchMaker;
        private readonly GlobalHeaderPresenter globalHeader;
        private readonly CompositeDisposable disposables = new();

        public HomePresenter(HomePage view, IPlayerAccountService playerService,
            IScreenNavigator screenNavigator, ISceneNavigator sceneNavigator,
            BattleEntryStore battleEntryStore, IBattleMatchMaker battleMatchMaker, GlobalHeaderPresenter globalHeader)
        {
            this.view = view;
            this.playerService = playerService;
            this.screenNavigator = screenNavigator;
            this.sceneNavigator = sceneNavigator;
            this.battleEntryStore = battleEntryStore;
            this.battleMatchMaker = battleMatchMaker;
            this.globalHeader = globalHeader;
        }

        // HomeはPush時のViewDtoを持たず、自分でIPlayerAccountServiceから初期データを取得する。
        public UniTask InitializeAsync()
        {
            view.Refresh(CreateDto());
            // ホームは一番下の画面なので、戻るは出さない。
            globalHeader.Attach(this, new GlobalHeaderOptions { Title = "ホーム", ShowsBack = false })
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
            return new HomeViewDto { PlayerId = player.PlayerId };
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
