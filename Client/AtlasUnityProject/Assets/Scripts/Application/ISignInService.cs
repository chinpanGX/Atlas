using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // プレイヤーのサインイン。端末にアカウント情報が無ければサインアップしてからサインインする。デバイス登録と
    // トークンの発行は、最初の通信で通信の共通処理が自動で行う。成功すると所持データが各Repositoryに入り、
    // IPlayerAccountService経由の読み出しができる状態になる。
    public interface ISignInService
    {
        UniTask<SignInOutcome> SignInAsync();
    }

    public enum SignInOutcome
    {
        SignedIn,

        // 通信の失敗。ダイアログ・画面遷移は通信の共通処理が済ませているので、呼び出し側は案内しなくてよい。
        Failed,

        // 端末に保存したアカウント情報が壊れている。保存し直しても直らないので、新しく始める等の案内が要る。
        AccountDataCorrupted,

        // デバイス登録はできたが、端末に保存できなかった(空き容量不足など)。もう一度試せる。
        AccountSaveFailed,

        // 保存したアカウント情報をいまは読めない(ほかのプロセスが開いている等)。もう一度試せる。
        AccountUnreadable,
    }
}
