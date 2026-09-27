using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Battle
{
    // 「けってい」で選んだパチモン(選んだ順)をOnConfirmedで流して相手の選出待ちの表示にするだけで、選出の送信は
    // Push元(BattlePresenter)が行う。自分からは閉じず、Push元が対戦の開始(OnMatchStart)か決着(選出の時間切れ等)を
    // 受けた時にCloseAsyncで閉じる(相手の選出が済むまでは、この画面のまま待たせるため)。
    [AssetAddress(AddressDefinition.SelectionModal)]
    public sealed class SelectionPresenter : IScreenWithArgs<SelectionViewDto>
    {
        private readonly SelectionModal view;
        private readonly SelectionViewDto initialDto;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();
        private readonly CancellationTokenSource countdownCancellation = new();
        private readonly Subject<string[]> confirmed = new();

        // 選んだ順の候補インデックス(SelectionViewDto.SelfParty)。
        private readonly List<int> selectedIndexes = new();
        private int remainingSeconds;
        // 「けってい」を押してから、送信に失敗して選び直すことになるまで。この間は選び直せない。
        private bool waitingForOpponent;
        private bool closing;
        private UniTask closeTask;

        public SelectionPresenter(SelectionModal view, SelectionViewDto initialDto, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.initialDto = initialDto;
            this.screenNavigator = screenNavigator;
        }

        // 「けってい」で選んだPlayerPachimonId(選んだ順、先頭が最初に場に出る)。
        public Observable<string[]> OnConfirmed => confirmed;

        private bool CanConfirm =>
            !waitingForOpponent && selectedIndexes.Count == initialDto.SelectionCount && remainingSeconds > 0;

        public UniTask InitializeAsync()
        {
            remainingSeconds = initialDto.RemainingSeconds;
            view.Refresh(initialDto);
            view.SetRemainingSeconds(remainingSeconds);
            RefreshSelection();
            if (initialDto.SelfParty.Count > 0)
            {
                view.ShowInfo(initialDto.SelfParty[0].Info);
            }

            view.OnCandidateClicked.Subscribe(ToggleCandidate).AddTo(disposables);
            view.OnConfirmButtonClicked.Where(_ => CanConfirm).Subscribe(_ => Confirm()).AddTo(disposables);

            CountdownAsync(countdownCancellation.Token).Forget();
            return UniTask.CompletedTask;
        }

        // 送信に失敗した場合に呼ぶ。選んだ内容は残したまま、もう一度「けってい」を押せる状態に戻す。
        public void CancelWaiting()
        {
            waitingForOpponent = false;
            RefreshSelection();
        }

        // 対戦の開始・決着でPush元から閉じる。2回目以降は進行中の閉じる処理の完了を待つだけにする(二重にPopしない)。
        public UniTask CloseAsync()
        {
            if (!closing)
            {
                closing = true;
                closeTask = screenNavigator.PopModalAsync(this).Preserve();
            }

            return closeTask;
        }

        public UniTask<object> CompleteAsync()
        {
            return UniTask.FromResult<object>(null);
        }

        private void Confirm()
        {
            waitingForOpponent = true;
            RefreshSelection();
            confirmed.OnNext(selectedIndexes.Select(i => initialDto.SelfParty[i].PlayerPachimonId).ToArray());
        }

        // 押した候補の詳細を中央に出し、選ばれていなければ末尾に加え、選ばれていれば外す(後ろの順番は
        // 1つずつ繰り上がる)。選び終わった状態で未選択の候補を押した場合と、相手の選出待ちの間は、詳細を出すだけにする。
        private void ToggleCandidate(int index)
        {
            view.ShowInfo(initialDto.SelfParty[index].Info);
            if (waitingForOpponent)
            {
                return;
            }

            if (!selectedIndexes.Remove(index))
            {
                if (selectedIndexes.Count >= initialDto.SelectionCount)
                {
                    return;
                }

                selectedIndexes.Add(index);
            }

            RefreshSelection();
        }

        private void RefreshSelection()
        {
            view.SetSelection(selectedIndexes, initialDto.SelectionCount, CanConfirm);
            if (waitingForOpponent)
            {
                view.ShowWaitingForOpponent();
            }
        }

        private async UniTaskVoid CountdownAsync(CancellationToken cancellation)
        {
            while (remainingSeconds > 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: cancellation);
                remainingSeconds--;
                view.SetRemainingSeconds(remainingSeconds);
                if (remainingSeconds == 0)
                {
                    RefreshSelection();
                }
            }
        }

        public void Dispose()
        {
            countdownCancellation.Cancel();
            countdownCancellation.Dispose();
            confirmed.Dispose();
            disposables.Dispose();
        }
    }
}
