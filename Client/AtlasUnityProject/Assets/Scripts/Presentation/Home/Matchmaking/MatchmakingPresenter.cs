using Atlas.Application.Address;
using R3;
using UnityScreenNavigator;

namespace Atlas.Presentation.Home
{
    // マッチングの開始・キャンセル・成立後の遷移はPush元のHomePresenterが行うため、キャンセルボタンの押下を渡すだけ。
    [AssetAddress(AddressDefinition.MatchmakingModal)]
    public sealed class MatchmakingPresenter : IPresenter
    {
        private readonly MatchmakingModal view;

        public MatchmakingPresenter(MatchmakingModal view)
        {
            this.view = view;
        }

        public Observable<Unit> OnCancelButtonClicked => view.OnCancelButtonClicked;

        public void Dispose()
        {
        }
    }
}
