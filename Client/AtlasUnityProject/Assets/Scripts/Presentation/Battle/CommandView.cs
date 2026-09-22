using System.Collections.Generic;
using System.Linq;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Battle
{
    public sealed class CommandView : MonoBehaviour
    {
        [SerializeField] private Button[] commandButtons;
        [SerializeField] private TextMeshProUGUI[] commandLabels;

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
                    commandLabels[slot].text = command.Name;
                }
            }
        }
    }
}
