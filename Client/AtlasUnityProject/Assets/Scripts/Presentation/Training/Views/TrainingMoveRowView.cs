using Atlas.Presentation.Common;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Training
{
    // 技タブの、覚えている技1つの行。右の「付け替える」で、その枠に入れる技の候補を開く。
    public sealed class TrainingMoveRowView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private PachimonTypeBadgeView typeBadge;
        [SerializeField] private TextMeshProUGUI categoryText;
        [SerializeField] private TextMeshProUGUI detailText;
        [SerializeField] private CommonButton replaceButton;

        public Observable<Unit> OnReplaceButtonClicked => replaceButton.OnClickAsObservable();

        public void Refresh(TrainingMoveDto move)
        {
            gameObject.SetActive(true);
            nameText.text = move.Name;
            typeBadge.Show(move.Type);
            categoryText.text = move.CategoryName;
            detailText.text = $"威力 {move.BasePower}　命中 {move.Accuracy}　PP {move.MaxPp}";
        }

        // 技を覚えていない枠は行ごと隠す。
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
