using TMPro;
using UnityEngine;

namespace Atlas.Presentation.Party
{
    public sealed class PachimonMoveRowView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI typeText;
        [SerializeField] private TextMeshProUGUI ppText;

        public void Refresh(PachimonMoveDto move)
        {
            nameText.text = move.Name;
            typeText.text = move.TypeName;
            ppText.text = $"{move.CurrentPp}/{move.MaxPp}";
        }

        public void Clear()
        {
            nameText.text = string.Empty;
            typeText.text = string.Empty;
            ppText.text = string.Empty;
        }
    }
}
