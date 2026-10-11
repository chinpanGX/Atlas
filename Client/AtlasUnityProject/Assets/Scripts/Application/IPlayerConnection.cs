using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // サインアップ・サインイン。通信の失敗のダイアログ・画面遷移は通信の共通処理が済ませているので、
    // 呼び出し側は成否だけを見ればよい。サインインの所持データの全件は、実装側が各Repositoryへ反映する。
    public interface IPlayerConnection
    {
        // 作成済みなら何も変えずに成功する。
        UniTask<bool> SignUpAsync(string nickname);

        // プレイヤーがまだ作られていなければ、ダイアログを出さずにStatusがPlayerNotFoundの結果を返す。
        UniTask<SignInResult> SignInAsync();
    }

    public enum SignInStatus
    {
        SignedIn,
        PlayerNotFound,
        Failed,
    }

    public sealed class SignInResult
    {
        public readonly SignInStatus Status;
        public readonly string PlayerId;
        public readonly string Nickname;

        private SignInResult(SignInStatus status, string playerId, string nickname)
        {
            Status = status;
            PlayerId = playerId;
            Nickname = nickname;
        }

        public static SignInResult SignedIn(string playerId, string nickname)
        {
            return new SignInResult(SignInStatus.SignedIn, playerId, nickname);
        }

        public static SignInResult PlayerNotFound()
        {
            return new SignInResult(SignInStatus.PlayerNotFound, string.Empty, string.Empty);
        }

        public static SignInResult Failed()
        {
            return new SignInResult(SignInStatus.Failed, string.Empty, string.Empty);
        }
    }
}
