using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    // 技パネルの技ボタン1つ分。技名・タイプ・PP(残り/最大)を表示する。
    public sealed class MoveCommandView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI typeText;
        [SerializeField] private TextMeshProUGUI ppText;

        private bool hasPp;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void Refresh(CommandDto command)
        {
            nameText.text = command.Name;
            typeText.text = command.TypeName;
            ppText.text = $"{command.CurrentPp}/{command.MaxPp}";
            hasPp = command.CurrentPp > 0;
        }

        // PPが0の技は、入力を受け付ける状態でも押せないままにする(BattleServerはPP0の技を拒否する)。
        public void SetInteractable(bool interactable)
        {
            button.interactable = interactable && hasPp;
        }
    }
}
