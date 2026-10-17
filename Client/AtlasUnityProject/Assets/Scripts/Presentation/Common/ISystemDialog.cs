using Cysharp.Threading.Tasks;

namespace Atlas.Presentation.Common
{
    // APIサーバーとの通信以外(対戦の接続など)の失敗を知らせるダイアログ。通信エラーのダイアログと同じく
    // Bootstrapシーンに常駐し、シーンの切り替え中でも出せる。ボタンは1つで、押されたら閉じる。
    public interface ISystemDialog
    {
        UniTask ShowAsync(string title, string message, string buttonLabel = "OK");
    }
}
