using System;
using Cysharp.Threading.Tasks;
using R3;
using VContainer.Unity;
using ZLinq;

namespace Atlas.Presentation.Battle
{
    // 選ばれた交代先(またはやめた)をPop結果として返すだけで、交代の送信はPush元
    // (BattlePresenter)がWaitForResultAsyncの結果を見て行う(ForfeitConfirmPresenterと同じ方針)。
    public sealed class SwitchSelectPresenter : IInitializable, IDisposable
    {
        private readonly SwitchSelectModal view;
        private readonly SwitchSelectViewDto initialDto;
        private readonly CompositeDisposable disposables = new();

        private SwitchCandidateDto selected;

        public SwitchSelectPresenter(SwitchSelectModal view, SwitchSelectViewDto initialDto)
        {
            this.view = view;
            this.initialDto = initialDto;
        }

        public void Initialize()
        {
            view.Refresh(initialDto);

            // 最初は交代できる先頭の控えを選択しておく(強制交代では選ぶだけで確定できる状態にする)。
            // 交代できる控えが無い場合は場のパチモンの詳細を出しておく。
            Select(initialDto.Candidates.FirstOrDefault(c => c.CanSwitchTo)
                   ?? initialDto.Candidates.First(c => c.IsActive));

            view.OnCandidateClicked
                .Subscribe(slot => Select(initialDto.Candidates.First(c => c.PartySlot == slot)))
                .AddTo(disposables);

            // 確定/やめるのどちらかを1回押した時点で閉じる(Pop中に再度押されて二重にPopしないようにする)。
            view.OnConfirmButtonClicked.Select(_ => SwitchSelectResult.Selected(selected.PartySlot))
                .Merge(view.OnCancelButtonClicked.Select(_ => SwitchSelectResult.Canceled))
                .Take(1)
                .Subscribe(result => view.CompleteAsync(result).Forget())
                .AddTo(disposables);
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
