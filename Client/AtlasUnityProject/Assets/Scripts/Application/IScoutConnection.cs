using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // スカウト(GET /scout/banners, POST /scout/rolls, POST /scout/rolls/{rollId}/select)。
    // ジェムの消費・入手したパチモンはplayerDiffとして実装側が各Repositoryへ反映する。
    public interface IScoutConnection
    {
        // 開催中のバナー一覧。排出率はサーバーが返さないため持たない。
        UniTask<IReadOnlyList<ScoutBanner>> GetBannersAsync();

        // ジェムを消費して候補10体をロールする。ジェムの消費はこの時点で確定し、選ばずに離れても戻らない。
        UniTask<ScoutRoll> RollAsync(string bannerId);

        // 候補から1体を選んで入手する。1つのロールにつき1回だけ呼べる。
        UniTask SelectAsync(string rollId, int candidateIndex);
    }
}
