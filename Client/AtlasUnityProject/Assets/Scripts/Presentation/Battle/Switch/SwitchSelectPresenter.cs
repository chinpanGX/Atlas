using System.Linq;
using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Battle
{
    // 選ばれた交代先(またはやめた)をPop結果として返すだけで、交代の送信はPush元
    // (BattlePresenter)がWaitForPopAsyncの結果を見て行う(ForfeitConfirmPresenterと同じ方針)。
    [AssetAddress(AddressDefinition.SwitchSelectModal)]
    public sealed class SwitchSelectPresenter : IScreenWithArgs<SwitchSelectViewDto>
    {
        private readonly SwitchSelectModal view;
        private readonly SwitchSelectViewDto initialDto;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private SwitchCandidateDto selected;
        private SwitchSelectResult result;
        private UniTask closeTask;

        public SwitchSelectPresenter(SwitchSelectModal view, SwitchSelectViewDto initialDto,
            IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.initialDto = initialDto;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.Refresh(initialDto);

            // 最初は交代できる先頭の控えを選択しておく(強制交代では選ぶだけで確定できる状態にする)。
            // 交代できる控えが無い場合は場のパチモンの詳細を出しておく。
            Select(initialDto.Candidates.FirstOrDefault(c => c.CanSwitchTo)
                   ?? initialDto.Candidates.First(c => c.IsActive));

            view.OnCandidateClicked
                .Subscribe(slot => Select(initialDto.Candidates.First(c => c.PartySlot == slot)))
                .AddTo(disposables);

            view.OnConfirmButtonClicked.Select(_ => SwitchSelectResult.Selected(selected.PartySlot))
                .Merge(view.OnCancelButtonClicked.Select(_ => SwitchSelectResult.Canceled))
                .Take(1)
                .Subscribe(value => CloseAsync(value).Forget())
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 結果を確定して閉じる。ボタンでの確定とPush元からの強制クローズ(ターンの進行・決着)が重なった場合は
        /// 最初の結果を優先し、2回目以降は進行中の閉じる処理の完了を待つだけにする(同じ画面を二重にPopしない)。
        /// </summary>
        public UniTask CloseAsync(SwitchSelectResult value)
        {
            if (result is null)
            {
                result = value;
                closeTask = screenNavigator.PopModalAsync(this).Preserve();
            }

            return closeTask;
        }

        // CloseAsyncを経由せずに閉じられた場合(背景タップ等)はやめた扱い。
        public UniTask<object> CompleteAsync()
        {
            return UniTask.FromResult<object>(result ?? SwitchSelectResult.Canceled);
        }

        private void Select(SwitchCandidateDto candidate)
        {
            selected = candidate;
            view.Select(candidate);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
