using Cysharp.Threading.Tasks;

namespace Atlas.Domain
{
    // サインインで受け取ったプレイヤーの情報。
    public interface IPlayerProfileRepository
    {
        PlayerProfile Get();

        UniTask SaveAsync(PlayerProfile profile);

        // タイトルに戻ってサインインし直すときに消す。
        void Clear();
    }
}
