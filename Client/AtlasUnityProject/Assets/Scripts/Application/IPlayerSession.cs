namespace Atlas.Application
{
    // サインインしているか。タイトルに戻るとき(通信エラーの後の画面遷移を含む)にサインアウトし、
    // 手元の所持データを捨てる。通信を持たないので、通信の共通処理からも循環せずに使える。
    public interface IPlayerSession
    {
        bool IsSignedIn { get; }

        void MarkSignedIn();

        // サインインし直したときにサーバーで無くなったものが残らないよう、手元の所持データを捨てる。
        void SignOut();
    }
}
