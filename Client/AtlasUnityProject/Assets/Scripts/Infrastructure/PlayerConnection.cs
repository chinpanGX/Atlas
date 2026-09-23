using System.Linq;
using Atlas.Application;
using Atlas.Domain;
using Atlas.Infrastructure.Api;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class PlayerConnection : IPlayerConnection
    {
        private readonly PlayerApiClient client;

        public PlayerConnection(string baseUrl, AccessTokenStore accessTokenStore)
        {
            client = new PlayerApiClient(baseUrl, () => accessTokenStore.CurrentToken);
        }

        public UniTask SignUpAsync(string nickname)
        {
            return client.SignupAsync(new CreatePlayerRequest { Nickname = nickname });
        }

        public async UniTask<SignInResult> SignInAsync()
        {
            var response = await client.SignInAsync();

            // playerDiffのうちpachimon/pachimonMoveMap/partySlotsは現状どこからも参照されない
            // (Scout/Party画面実装時に、それぞれ専用のリポジトリ向けとして扱う。architecture.md
            // 「APIレスポンス設計」参照)ため、ここではitemsだけを取り出す。
            var upsertedItems = response.PlayerDiff.Items.Upserted
                .Select(i => new Item(i.ItemId, i.Quantity))
                .ToArray();

            return new SignInResult(response.PlayerId, response.Nickname, upsertedItems, response.PlayerDiff.Items.Removed);
        }
    }
}
