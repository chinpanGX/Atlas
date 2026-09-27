using TMPro;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    // 選出Modal右側の、相手のパーティ1体分(表示のみ)。
    public sealed class SelectionOpponentView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI typeText;

        public void Refresh(SelectionOpponentDto opponent)
        {
            gameObject.SetActive(true);
            nameText.text = opponent.Name;
            typeText.text = string.Join(" / ", opponent.TypeNames);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
