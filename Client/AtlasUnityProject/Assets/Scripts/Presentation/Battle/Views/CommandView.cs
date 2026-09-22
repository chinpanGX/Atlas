using System.Collections.Generic;
using System.Linq;
using R3;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    public sealed class CommandView : MonoBehaviour
    {
        [SerializeField] private CommonButton[] commandButtons;

        public IReadOnlyList<Observable<Unit>> OnCommandButtonClicked { get; private set; }

        private void Awake()
        {
            OnCommandButtonClicked = commandButtons.Select(button => button.OnClickAsObservable()).ToArray();
        }

        // SlotNoが無い(=対応するコマンドが無い)枠のボタンは非表示にする(元のBattlePage.SetMoveButton
        // と同じ方針)。
        public void Refresh(IReadOnlyList<CommandDto> commands)
        {
            for (var slot = 0; slot < commandButtons.Length; slot++)
            {
                var command = commands.FirstOrDefault(c => c.SlotNo == slot);
                commandButtons[slot].gameObject.SetActive(command is not null);
                if (command is not null)
                {
                    commandButtons[slot].SetTextInChildSafe(command.Name);
                }
            }
        }
    }
}
