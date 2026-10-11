using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // プレイヤーのサインイン(未作成なら作ってからサインインし直す)。デバイス登録とトークンの発行は、
    // 最初の通信で通信の共通処理が自動で行う。成功すると所持データが各Repositoryに入り、
    // IPlayerAccountService経由の読み出しができる状態になる。
    public interface ISignInService
    {
        // 失敗したときのダイアログ・画面遷移は通信の共通処理が済ませているので、成否だけを返す。
        UniTask<bool> SignInAsync();
    }
}
