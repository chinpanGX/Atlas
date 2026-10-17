using System;
using System.Collections.Generic;
using Atlas.Application;
using Atlas.Presentation.Shop;
using Cysharp.Threading.Tasks;
using R3;
using Supplement.Core;
using UnityScreenNavigator;
using VContainer.Unity;

namespace Atlas.Presentation.GlobalHeader
{
    // Homeシーンに1つだけ置くヘッダーを、表示中の画面に合わせて切り替える。ヘッダーを使う画面のPresenterは
    // 開くときにAttachし、最後にAttachした(一番上に積まれた)画面の設定で表示する。
    public sealed class GlobalHeaderPresenter : IStartable, IDisposable
    {
        // itemsマスタのitem_id=1(ジェム)、2(育成チケット)。
        private const int GemItemId = 1;
        private const int TicketItemId = 2;

        private readonly GlobalHeaderView view;
        private readonly IItemService itemService;
        private readonly IMessageBroker messageBroker;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();
        // 積まれた順(一番下がホーム)。
        private readonly List<GlobalHeaderHandle> handles = new();

        private bool isLeaving;
        private bool isDisposed;

        public GlobalHeaderPresenter(GlobalHeaderView view, IItemService itemService, IMessageBroker messageBroker,
            IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.itemService = itemService;
            this.messageBroker = messageBroker;
            this.screenNavigator = screenNavigator;
        }

        public void Start()
        {
            Refresh();

            disposables.Add(messageBroker.Subscribe<ItemQuantityChanged>(_ => Refresh()));
            view.OnBackButtonClicked
                .SubscribeAwait(async (_, _) => await BackAsync(), AwaitOperation.Drop)
                .AddTo(disposables);
            // 今の画面の上に開き、戻ると元の画面に戻る。Push完了までの連打で重ねて積まないよう、実行中の押下は捨てる。
            view.OnShopButtonClicked
                .SubscribeAwait(async (_, _) => await screenNavigator.PushPageAsync<ShopPresenter>(), AwaitOperation.Drop)
                .AddTo(disposables);
        }

        public GlobalHeaderHandle Attach(IPresenter owner, GlobalHeaderOptions options)
        {
            var handle = new GlobalHeaderHandle(this, owner, options);
            handles.Add(handle);
            Refresh();
            return handle;
        }

        internal void Detach(GlobalHeaderHandle handle)
        {
            handles.Remove(handle);
            Refresh();
        }

        internal void Refresh()
        {
            // シーンの破棄では、ヘッダー(と、そのView)が先に破棄されてから画面がDetachしてくることがある。
            if (isDisposed || view == null)
            {
                return;
            }

            if (handles.Count == 0)
            {
                view.Refresh(new GlobalHeaderViewDto { IsVisible = false });
                return;
            }

            var top = handles[^1];
            view.Refresh(new GlobalHeaderViewDto
            {
                IsVisible = true,
                Title = top.Options.Title,
                Gems = itemService.GetAmount(GemItemId),
                Tickets = itemService.GetAmount(TicketItemId),
                ShowsBack = top.Options.ShowsBack,
                CanOpenShop = top.Options.CanOpenShop,
                IsInteractable = !isLeaving && !top.IsLocked,
            });
        }

        // 一番上の画面に離れてよいか確かめてから閉じる。確かめている間(保存の通信中など)はヘッダーを押せなくする。
        private async UniTask BackAsync()
        {
            if (handles.Count == 0)
            {
                return;
            }

            var top = handles[^1];
            isLeaving = true;
            Refresh();
            try
            {
                if (await top.LeaveAsync())
                {
                    await screenNavigator.PopPageAsync(top.Owner);
                }
            }
            finally
            {
                isLeaving = false;
                Refresh();
            }
        }

        public void Dispose()
        {
            isDisposed = true;
            disposables.Dispose();
        }
    }
}
