using System.Threading;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // 対戦相手を探す(POST /battle/queue → GET /battle/queue/statusで成立を待つ)。Mock(オフライン)と
    // Real(APIサーバー)の切り替え単位。
    public interface IBattleMatchmaker
    {
        // マッチが成立するまで待つ。cancellationがキャンセルされたら待機列から抜けてOperationCanceledExceptionを投げる。
        UniTask<BattleMatch> FindMatchAsync(CancellationToken cancellation);
    }
}
