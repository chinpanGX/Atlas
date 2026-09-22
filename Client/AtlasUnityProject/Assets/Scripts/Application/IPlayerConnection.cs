using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // サインアップ・サインイン(POST /signup, POST /sign-in)。IDeviceConnectionと同様、
    // 唯一の実装しか存在せずMock/Realの切り替えを行わないため、Application層のポートとして
    // 定義する(「Repository」と呼ばない。Entityの永続化ではなく認証アクション呼び出しのため)。
    public interface IPlayerConnection
    {
        UniTask SignUpAsync(string nickname);

        UniTask<SignInResult> SignInAsync();
    }

    public sealed class SignInResult
    {
        public readonly string PlayerId;
        public readonly string Nickname;

        public SignInResult(string playerId, string nickname)
        {
            PlayerId = playerId;
            Nickname = nickname;
        }
    }
}
