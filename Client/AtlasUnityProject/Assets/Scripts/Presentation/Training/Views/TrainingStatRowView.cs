using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Training
{
    // ステータスタブの能力1つの行。左から能力名・−/ポイント/＋・確定済みからの変化・ゲージ・能力の値。
    public sealed class TrainingStatRowView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI labelText;
        [SerializeField] private CommonButton decreaseButton;
        [SerializeField] private TextMeshProUGUI pointsText;
        [SerializeField] private CommonButton increaseButton;
        // 増やした分(緑)と減らした分(灰色)は色が違うため、テキストを分けて出し分ける。
        [SerializeField] private TextMeshProUGUI increasedText;
        [SerializeField] private TextMeshProUGUI decreasedText;
        // ゲージ本体。fillAmountで長さを表す(スプライトを設定したFilledのImage)。
        [SerializeField] private Image pointsGaugeFill;
        [SerializeField] private TextMeshProUGUI valueText;

        public Observable<Unit> OnDecreaseButtonClicked => decreaseButton.OnClickAsObservable();
        public Observable<Unit> OnIncreaseButtonClicked => increaseButton.OnClickAsObservable();

        public void Refresh(TrainingStatDto stat)
        {
            labelText.text = stat.Label;
            pointsText.text = stat.Points.ToString();
            decreaseButton.interactable = stat.CanDecrease;
            increaseButton.interactable = stat.CanIncrease;

            increasedText.gameObject.SetActive(stat.Change > 0);
            increasedText.text = $"+{stat.Change}";
            decreasedText.gameObject.SetActive(stat.Change < 0);
            decreasedText.text = $"−{-stat.Change}";

            pointsGaugeFill.fillAmount = stat.PointsRatio;
            valueText.text = stat.CommittedValue == stat.AdjustedValue
                ? stat.CommittedValue.ToString()
                : $"{stat.CommittedValue} → <b>{stat.AdjustedValue}</b>";
        }
    }
}
