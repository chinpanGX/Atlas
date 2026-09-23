using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Battle
{
    public sealed class BattleResultModal : Modal
    {
        [SerializeField] private TextMeshProUGUI resultText;
        [SerializeField] private TextMeshProUGUI reasonText;
        [SerializeField] private Button homeButton;

        public Observable<Unit> OnHomeButtonClicked => homeButton.OnClickAsObservable();

        public void Refresh(BattleResultViewDto dto)
        {
            resultText.text = dto.ResultText;
            reasonText.text = dto.ReasonText;
        }
    }
}
