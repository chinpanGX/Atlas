using System.Threading;
using Cysharp.Threading.Tasks;

namespace Atlas.Application
{
    // 対戦相手を探す(待機列に入り、成立するまで待つ)。Mock(オフライン)とReal(APIサーバー)の切り替え単位。
    public interface IBattleMatchmaker
    {
        // マッチが成立するまで待つ。通信が失敗したら(ダイアログは通信の共通処理が出している)nullを返す。
        // cancellationがキャンセルされたら待機列から抜けてOperationCanceledExceptionを投げる。
        UniTask<BattleMatch> FindMatchAsync(CancellationToken cancellation);
    }
}
