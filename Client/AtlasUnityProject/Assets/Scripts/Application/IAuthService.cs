using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // デバイス登録・認証・プレイヤー作成(design/outgame.md「デバイス認証」「プレイヤー作成」参照)
    // までの初回起動フローをまとめて呼び出す窓口。完了後はIPlayerRepository経由の呼び出しが
    // 成功する状態になる。
    public interface IAuthService
    {
        UniTask EnsureSignedUpAsync();
    }
}
