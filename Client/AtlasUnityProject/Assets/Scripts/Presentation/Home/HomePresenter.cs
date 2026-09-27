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
        private readonly IItemFetchService itemFetchService;
        private readonly IDebugConnection debugConnection;
        private readonly IScreenNavigator screenNavigator;
        private readonly ISceneNavigator sceneNavigator;
        private readonly BattleEntryStore battleEntryStore;
        private readonly IBattleMatchmaker battleMatchmaker;
        private readonly CompositeDisposable disposables = new();

        public HomePresenter(HomePage view, IPlayerAccountService playerService, IItemFetchService itemFetchService,
            IDebugConnection debugConnection, IScreenNavigator screenNavigator, ISceneNavigator sceneNavigator,
            BattleEntryStore battleEntryStore, IBattleMatchmaker battleMatchmaker)
        {
            this.view = view;
            this.playerService = playerService;
            this.itemFetchService = itemFetchService;
            this.debugConnection = debugConnection;
            this.screenNavigator = screenNavigator;
            this.sceneNavigator = sceneNavigator;
            this.battleEntryStore = battleEntryStore;
            this.battleMatchmaker = battleMatchmaker;
        }

        // HomeはPush時のViewDtoを持たず、自分でIPlayerAccountServiceから初期データを取得する。
        public UniTask InitializeAsync()
        {
            view.Refresh(CreateDto());

            // 動作確認用。連打でも1回分ずつ送るよう、実行中の押下は待たせる(捨てない)。
            view.OnGrantGemsButtonClicked
                .SubscribeAwait(async (_, _) =>
                {
                    await debugConnection.GrantGemsAsync();
                    view.Refresh(CreateDto());
                }, AwaitOperation.Sequential)
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
            // チャット画面は未実装のため、現時点ではログのみ。
            view.OnChatButtonClicked.Subscribe(_ => Debug.Log("[Home] Chat button clicked (not implemented yet)")).AddTo(disposables);
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
            var gems = itemFetchService.GetAmount(GemItemId);
            return new HomeViewDto { PlayerId = player.PlayerId, Gems = gems };
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
