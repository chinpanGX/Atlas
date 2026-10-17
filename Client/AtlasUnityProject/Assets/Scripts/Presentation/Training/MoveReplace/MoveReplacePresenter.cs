using Atlas.Application.Address;
using Cysharp.Threading.Tasks;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Training
{
    // 選んだ技をPop結果として返すだけで、付け替えの送信はPushした側(TrainingPresenter)が行う。
    // 「やめる」以外の閉じ方(背景タップ等)も付け替えない扱い(結果はnull)。
    // 下の欄で変化を見せたうえで、選ぶ・押すの2段階にしているので、別の確認ダイアログは挟まない。
    [AssetAddress(AddressDefinition.MoveReplaceModal)]
    public sealed class MoveReplacePresenter : IScreenWithArgs<MoveReplaceViewDto>
    {
        private readonly MoveReplaceModal view;
        private readonly MoveReplaceViewDto dto;
        private readonly IScreenNavigator screenNavigator;
        private readonly CompositeDisposable disposables = new();

        private int? selectedIndex;
        private MoveReplaceResult result;

        public MoveReplacePresenter(MoveReplaceModal view, MoveReplaceViewDto dto, IScreenNavigator screenNavigator)
        {
            this.view = view;
            this.dto = dto;
            this.screenNavigator = screenNavigator;
        }

        public UniTask InitializeAsync()
        {
            view.Refresh(dto);

            view.OnCandidateClicked
                .Subscribe(index =>
                {
                    selectedIndex = index;
                    view.RefreshSelection(selectedIndex);
                })
                .AddTo(disposables);

            // どちらか一方を1回押した時点で閉じる(Pop中に再度押されて二重にPopしないようにする)。
            view.OnReplaceButtonClicked.Select(_ => true)
                .Merge(view.OnCancelButtonClicked.Select(_ => false))
                .Take(1)
                .Subscribe(replaces =>
                {
                    if (replaces && selectedIndex is { } index)
                    {
                        result = new MoveReplaceResult(dto.Candidates[index].MoveId);
                    }
                    screenNavigator.PopModalAsync(this).Forget();
                })
                .AddTo(disposables);
            return UniTask.CompletedTask;
        }

        public UniTask<object> CompleteAsync()
        {
            return UniTask.FromResult<object>(result);
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
