using Atlas.Application.Address;
using UnityScreenNavigator;

namespace Atlas.Presentation.Battle
{
    // 表示のみで操作は無い。閉じるのはPush元のBattlePresenter。
    [AssetAddress(AddressDefinition.OpponentSwitchingModal)]
    public sealed class OpponentSwitchingPresenter : IPresenter
    {
        public void Dispose()
        {
        }
    }
}
