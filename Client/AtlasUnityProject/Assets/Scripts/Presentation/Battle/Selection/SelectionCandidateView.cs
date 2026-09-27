using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    // 選出Modal中央の、自分のパーティ1体分。タップで選出に入れる/外す。選んだ順番(1始まり)を表示する。
    public sealed class SelectionCandidateView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI orderText;
        [SerializeField] private GameObject selectedFrame;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void Refresh(SelectionCandidateDto candidate)
        {
            gameObject.SetActive(true);
            nameText.text = candidate.Name;
            SetOrder(null);
        }

        // orderは選んだ順番(1始まり)。選ばれていなければnull。
        public void SetOrder(int? order)
        {
            selectedFrame.SetActive(order.HasValue);
            orderText.text = order?.ToString() ?? string.Empty;
        }

        // パーティが6体未満の場合など、候補が無い枠は隠す。
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
