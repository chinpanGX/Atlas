using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Battle
{
    // 相手が強制交代で交代先を選んでいる間に出す、ボタンの無いModal。閉じるのはPushしたBattlePresenterで、
    // 相手の交代が済んだ(次のターン結果が届いた)ときに閉じる。
    public sealed class OpponentSwitchingModal : Modal
    {
    }
}
