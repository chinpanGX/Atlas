using System.Collections.Generic;
using System.Linq;
using Atlas.Application;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure.Api
{
    public sealed class PlayerConnection : IPlayerConnection
    {
        private readonly PlayerApiClient client;
        private readonly AccessTokenRefresher accessTokenRefresher;
        private readonly IPlayerDiffApplier playerDiffApplier;

        public PlayerConnection(
            string baseUrl,
            AccessTokenStore accessTokenStore,
            AccessTokenRefresher accessTokenRefresher,
            IPlayerDiffApplier playerDiffApplier)
        {
            client = new PlayerApiClient(baseUrl, () => accessTokenStore.CurrentToken);
            this.accessTokenRefresher = accessTokenRefresher;
            this.playerDiffApplier = playerDiffApplier;
        }

        public UniTask SignUpAsync(string nickname)
        {
            return accessTokenRefresher.SendAsync(
                () => client.SignupAsync(new CreatePlayerRequest { Nickname = nickname }));
        }

        public async UniTask<SignInResult> SignInAsync()
        {
            var response = await accessTokenRefresher.SendAsync(() => client.SignInAsync());
            await playerDiffApplier.ApplyAsync(response.PlayerDiff);
            return new SignInResult(response.PlayerId, response.Nickname);
        }

        public async UniTask EditPartyAsync(IReadOnlyList<PartySlotInput> slots)
        {
            var request = new SetPartyRequest
            {
                PartySlots = slots
                    .Select(s => new PartySlotRequest { Slot = s.Slot, PlayerPachimonId = s.PlayerPachimonId })
                    .ToList(),
            };
            var playerDiff = await accessTokenRefresher.SendAsync(() => client.EditPartyAsync(request));
            await playerDiffApplier.ApplyAsync(playerDiff);
        }
    }
}
