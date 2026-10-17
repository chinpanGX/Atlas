using System;
using System.Collections.Generic;
using System.Threading;
using Atlas.Application;
using Atlas.Application.Address;
using Atlas.Presentation.GlobalHeader;
using Cysharp.Threading.Tasks;
using MasterMemory;
using R3;
using UnityEngine;
using UnityScreenNavigator;

namespace Atlas.Presentation.Shop
{
    // 商品の購入ボタンを押すと、買う前と後の所持数を並べた確認Modalを出し、「購入する」で購入する。
    // 購入した後もショップに残り、買える・買えないの表示を購入後のジェムで出し直す。
    [AssetAddress(AddressDefinition.ShopPage)]
    public sealed class ShopPresenter : IPresenter
    {
        private readonly ShopPage view;
        private readonly IShopService shopService;
        private readonly IItemService itemService;
        private readonly IDebugConnectService debugConnectService;
        private readonly MemoryDatabase database;
        private readonly IScreenNavigator screenNavigator;
        private readonly GlobalHeaderPresenter globalHeader;
        private readonly CompositeDisposable disposables = new();
        private readonly CancellationTokenSource lifetimeCancellation = new();

        // 取得前・取得失敗時は空のまま。
        private IReadOnlyList<ShopProduct> products = Array.Empty<ShopProduct>();
        private GlobalHeaderHandle header;

        public ShopPresenter(
            ShopPage view,
            IShopService shopService,
            IItemService itemService,
            IDebugConnectService debugConnectService,
            MemoryDatabase database,
            IScreenNavigator screenNavigator,
            GlobalHeaderPresenter globalHeader)
        {
            this.view = view;
            this.shopService = shopService;
            this.itemService = itemService;
            this.debugConnectService = debugConnectService;
            this.database = database;
            this.screenNavigator = screenNavigator;
            this.globalHeader = globalHeader;
        }

        public UniTask InitializeAsync()
        {
            // ショップの上にショップを重ねないよう、ヘッダーのショップは出さない。
            header = globalHeader.Attach(this, new GlobalHeaderOptions { Title = "ショップ", CanOpenShop = false })
                .AddTo(disposables);
            view.SetDebugGrantGemsVisible(Debug.isDebugBuild);
            RefreshProducts();

            // 確認〜購入の通信の間の連打で、二重に確認を出したり購入したりしないよう、実行中の押下は捨てる。
            view.OnPurchaseButtonClicked
                .SubscribeAwait(async (index, ct) => await ConfirmAndPurchaseAsync(products[index], ct), AwaitOperation.Drop)
                .AddTo(disposables);
            // 動作確認用。連打でも1回分ずつ送るよう、実行中の押下は待たせる(捨てない)。
            view.OnDebugGrantGemsButtonClicked
                .SubscribeAwait(async (_, _) =>
                {
                    if (await debugConnectService.GrantGemsAsync() && !lifetimeCancellation.IsCancellationRequested)
                    {
                        RefreshProducts();
                    }
                })
                .AddTo(disposables);

            // 商品の取得を待たずに画面を開き、取得できた時点で商品を並べる。
            LoadProductsAsync(lifetimeCancellation.Token).Forget();
            return UniTask.CompletedTask;
        }

        private async UniTaskVoid LoadProductsAsync(CancellationToken cancellation)
        {
            var loaded = await shopService.GetProductsAsync();
            // 失敗したときのダイアログは通信の共通処理が出している。商品は並べないままにする
            if (loaded is null || cancellation.IsCancellationRequested)
            {
                return;
            }

            products = loaded;
            RefreshProducts();
        }

        private async UniTask ConfirmAndPurchaseAsync(ShopProduct product, CancellationToken cancellation)
        {
            var itemName = FindItemName(product.ItemId);
            var costItemBefore = itemService.GetAmount(product.CostItemId);
            var itemBefore = itemService.GetAmount(product.ItemId);
            var confirm = await screenNavigator.PushModalAsync<ShopConfirmPresenter, ShopConfirmViewDto>(
                new ShopConfirmViewDto
                {
                    ItemName = itemName,
                    Amount = product.Amount,
                    CostItemName = FindItemName(product.CostItemId),
                    CostAmount = product.CostAmount,
                    CostItemBefore = costItemBefore,
                    CostItemAfter = costItemBefore - product.CostAmount,
                    ItemBefore = itemBefore,
                    ItemAfter = itemBefore + product.Amount,
                });
            if (!await screenNavigator.WaitForPopAsync<bool>(confirm, cancellation))
            {
                return;
            }

            // 通信の途中で戻ると、結果を出す画面が無くなるため、終わるまでヘッダーを押せなくする。
            header.SetLocked(true);
            var purchased = await shopService.PurchaseAsync(product.ShopProductId);
            cancellation.ThrowIfCancellationRequested();
            header.SetLocked(false);
            if (!purchased)
            {
                return;
            }

            view.ShowToast($"{itemName}を{product.Amount}枚購入しました");
            RefreshProducts();
        }

        private void RefreshProducts()
        {
            var dtos = new List<ShopProductDto>(products.Count);
            foreach (var product in products)
            {
                dtos.Add(CreateProductDto(product));
            }
            view.Refresh(new ShopViewDto { Products = dtos });
        }

        private ShopProductDto CreateProductDto(ShopProduct product)
        {
            var unitPrice = (decimal)product.CostAmount / product.Amount;
            var baseProduct = FindBaseProduct(product);
            var baseUnitPrice = (decimal)baseProduct.CostAmount / baseProduct.Amount;
            return new ShopProductDto
            {
                ItemName = FindItemName(product.ItemId),
                Amount = product.Amount,
                UnitPrice = unitPrice,
                DiscountPercent = (int)Math.Round((1m - unitPrice / baseUnitPrice) * 100m),
                CostAmount = product.CostAmount,
                ShortageAmount = Math.Max(0, product.CostAmount - itemService.GetAmount(product.CostItemId)),
            };
        }

        // まとめ買いの割引率は、同じアイテムを同じ通貨で買う商品のうち、一番少ない数の商品の1枚あたりの値段と比べて出す。
        private ShopProduct FindBaseProduct(ShopProduct product)
        {
            var baseProduct = product;
            foreach (var other in products)
            {
                if (other.ItemId == product.ItemId && other.CostItemId == product.CostItemId && other.Amount < baseProduct.Amount)
                {
                    baseProduct = other;
                }
            }
            return baseProduct;
        }

        private string FindItemName(int itemId)
        {
            return database.ItemsDataTable.FindByItemId(itemId).Name;
        }

        public void Dispose()
        {
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            disposables.Dispose();
        }
    }
}
