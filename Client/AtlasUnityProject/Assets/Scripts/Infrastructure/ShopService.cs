using System.Collections.Generic;
using Atlas.Api.V1;
using Atlas.Application;
using Cysharp.Threading.Tasks;
using ShopProduct = Atlas.Application.ShopProduct;

namespace Atlas.Infrastructure.Grpc
{
    internal sealed class ShopService : IShopService
    {
        private readonly ShopApi shopApi;

        public ShopService(ShopApi shopApi)
        {
            this.shopApi = shopApi;
        }

        public async UniTask<IReadOnlyList<ShopProduct>> GetProductsAsync()
        {
            var result = await shopApi.ListProductsAsync(new ListProductsRequest());
            if (!result.IsSuccess)
            {
                return null;
            }

            var products = new List<ShopProduct>(result.Value.Products.Count);
            foreach (var product in result.Value.Products)
            {
                products.Add(new ShopProduct(product.ShopProductId, product.ItemId, product.Amount,
                    product.CostItemId, product.CostAmount));
            }
            return products;
        }

        public async UniTask<bool> PurchaseAsync(int shopProductId)
        {
            var result = await shopApi.PurchaseAsync(new PurchaseRequest { ShopProductId = shopProductId });
            return result.IsSuccess;
        }
    }
}
