using Atlas.Api.V1;
using Atlas.Application;
using Atlas.Infrastructure.Grpc;
using Cysharp.Threading.Tasks;
using VolTech.Client;

namespace Atlas.Infrastructure
{
    public sealed class PlayerSignInService : IPlayerSignInService
    {
        // サインインの「プレイヤー未作成」は、ダイアログを出さずにサインアップへ進むため自分で受け取る
        private static readonly ErrorCode[] signInHandledCodes = { AtlasErrors.PlayerNotFound };

        private readonly PlayerApi playerApi;
        private readonly PlayerSnapshotApplier snapshotApplier;

        public PlayerSignInService(PlayerApi playerApi, PlayerSnapshotApplier snapshotApplier)
        {
            this.playerApi = playerApi;
            this.snapshotApplier = snapshotApplier;
        }

        public async UniTask<bool> SignUpAsync(string nickname)
        {
            var result = await playerApi.SignUpAsync(new SignUpRequest { Nickname = nickname });
            return result.IsSuccess;
        }

        public async UniTask<SignInResult> SignInAsync()
        {
            var result = await playerApi.SignInAsync(new SignInRequest(), signInHandledCodes);
            if (!result.IsSuccess)
            {
                return result.Error.Code == AtlasErrors.PlayerNotFound
                    ? SignInResult.PlayerNotFound()
                    : SignInResult.Failed();
            }

            var response = result.Value;
            snapshotApplier.Replace(response.Snapshot);
            return SignInResult.SignedIn(response.PlayerId, response.Nickname);
        }
    }
}
