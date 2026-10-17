using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // ショップ。払ったジェムと受け取ったアイテムは、通信の共通処理がplayerDiffとしてRepositoryへ反映する。
    // 失敗したときのダイアログは通信の共通処理が済ませているので、失敗はnull・falseで返すだけにする。
    public interface IShopService
    {
        // 購入できる商品を、shop_product_idの順に返す。失敗したらnull。
        UniTask<IReadOnlyList<ShopProduct>> GetProductsAsync();

        // 商品を1つ購入する。
        UniTask<bool> PurchaseAsync(int shopProductId);
    }
}
