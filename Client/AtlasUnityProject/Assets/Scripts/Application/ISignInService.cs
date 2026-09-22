using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // デバイス登録・認証・プレイヤーのサインアップ/サインイン(design/outgame.md参照)までの
    // 初回起動フローをまとめて呼び出す窓口。通信リクエスト→レスポンス受信→各リポジトリへの反映
    // という一連の流れを1サービスで完結させる。完了後はIPlayerAccountService経由の読み出しが
    // 成功する状態になる。
    public interface ISignInService
    {
        UniTask SignInAsync();
    }
}
