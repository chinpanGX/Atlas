using System;
using System.Threading;
using Atlas.Domain;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Atlas.Presentation.Home
{
    public sealed class HomePresenter : IAsyncStartable, IDisposable
    {
        private readonly HomePage view;
        private readonly IPlayerConnection connection;
        private readonly CompositeDisposable disposables = new();

        public HomePresenter(HomePage view, IPlayerConnection connection)
        {
            this.view = view;
            this.connection = connection;
        }

        // HomeはPush時のViewDtoを持たず、自分でIPlayerConnectionから初期データを取得する。
        // 非同期の初期化が必要なためIInitializableではなくIAsyncStartableを使う
        // (同期で済む場合はTitlePresenterのようにIInitializableでよい)
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            var player = await connection.GetMeAsync(cancellation);
            view.Refresh(new HomeViewDto { Nickname = player.Nickname, Gems = player.Gems });

            // Scout/パーティ編成/バトル/チャットの各画面は未実装のため、現時点ではログのみ。
            // 各画面を実装するタイミングでIScreenNavigator.PushPageAsyncに置き換える
            view.OnScoutButtonClicked.Subscribe(_ => Debug.Log("[Home] Scout button clicked (not implemented yet)")).AddTo(disposables);
            view.OnPartyButtonClicked.Subscribe(_ => Debug.Log("[Home] Party button clicked (not implemented yet)")).AddTo(disposables);
            view.OnBattleButtonClicked.Subscribe(_ => Debug.Log("[Home] Battle button clicked (not implemented yet)")).AddTo(disposables);
            view.OnChatButtonClicked.Subscribe(_ => Debug.Log("[Home] Chat button clicked (not implemented yet)")).AddTo(disposables);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
