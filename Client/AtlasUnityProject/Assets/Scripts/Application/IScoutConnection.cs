using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // スカウト。ジェムの消費・入手したパチモンは、通信の共通処理がplayerDiffとして各Repositoryへ反映する。
    // 失敗したときのダイアログは通信の共通処理が済ませているので、失敗はnull・falseで返すだけにする。
    public interface IScoutConnection
    {
        // 開催中のバナー一覧。排出率はサーバーが返さないため持たない。失敗したらnull。
        UniTask<IReadOnlyList<ScoutBanner>> GetBannersAsync();

        // ジェムを消費して候補10体をロールする。ジェムの消費はこの時点で確定し、選ばずに離れても戻らない。失敗したらnull。
        UniTask<ScoutRoll> RollAsync(string bannerId);

        // 候補から1体を選んで入手する。1つのロールにつき1回だけ呼べる。
        UniTask<bool> SelectAsync(string rollId, int candidateIndex);
    }
}
