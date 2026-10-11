using Atlas.Application;
using Atlas.Domain;
using Cysharp.Threading.Tasks;

namespace Atlas.Infrastructure
{
    public sealed class SignInService : ISignInService
    {
        // ニックネーム入力画面は未実装のため、サインアップでは固定値を使う。
        private const string DefaultNickname = "プレイヤー";

        private readonly IPlayerConnection playerConnection;
        private readonly IPlayerProfileRepository playerProfileRepository;
        private readonly IPlayerSession playerSession;

        public SignInService(
            IPlayerConnection playerConnection,
            IPlayerProfileRepository playerProfileRepository,
            IPlayerSession playerSession)
        {
            this.playerConnection = playerConnection;
            this.playerProfileRepository = playerProfileRepository;
            this.playerSession = playerSession;
        }

        // サインアップ済みかは端末に覚えず、毎回サインインの結果で判断する。
        public async UniTask<bool> SignInAsync()
        {
            var signIn = await playerConnection.SignInAsync();
            if (signIn.Status == SignInStatus.PlayerNotFound)
            {
                if (!await playerConnection.SignUpAsync(DefaultNickname))
                {
                    return false;
                }
                signIn = await playerConnection.SignInAsync();
            }
            if (signIn.Status != SignInStatus.SignedIn)
            {
                return false;
            }

            await playerProfileRepository.SaveAsync(new PlayerProfile(signIn.PlayerId, signIn.Nickname));
            playerSession.MarkSignedIn();
            return true;
        }
    }
}
