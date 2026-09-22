using System.Linq;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    // サインイン・スカウト・パーティ編成・技の付け替えなど、playerDiffを返すAPIを実装する
    // 各Connection/Repository(同じくInfrastructure.Api配下)がレスポンス受信直後にここへ渡す。
    // リソース種別ごとのXxxDiffApplierへ振り分けるだけの窓口。
    public interface IPlayerDiffApplier
    {
        UniTask ApplyAsync(PlayerDiffDto dto);
    }

    public sealed class PlayerDiffApplier : IPlayerDiffApplier
    {
        private readonly ItemDiffApplier itemDiffApplier;

        public PlayerDiffApplier(ItemDiffApplier itemDiffApplier)
        {
            this.itemDiffApplier = itemDiffApplier;
        }

        public async UniTask ApplyAsync(PlayerDiffDto dto)
        {
            // pachimon/pachimonMoveMap/partySlotsはScout/Party画面実装時(progress.md残タスク#9)に
            // 同様のXxxDiffApplierを追加してここから呼ぶ。
            if (dto.Items != null) 
                await itemDiffApplier.ApplyAsync(dto.Items);
        }
    }

}
