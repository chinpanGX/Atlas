using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using ZLinq;

namespace Atlas.Presentation.Training
{
    // ステータスタブ。上に残りのポイント、中央に能力6つの行、下に必要なチケットと「元に戻す」「確定する」。
    public sealed class TrainingStatsView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI remainingPointsText;
        // 要素番号がHP・こうげき・ぼうぎょ・とくこう・とくぼう・すばやさの順に対応する。
        [SerializeField] private TrainingStatRowView[] statRows;
        [SerializeField] private TextMeshProUGUI ticketText;
        [SerializeField] private TextMeshProUGUI shortageText;
        [SerializeField] private CommonButton revertButton;
        [SerializeField] private CommonButton confirmButton;

        private Observable<int> onDecreaseButtonClicked;
        private Observable<int> onIncreaseButtonClicked;

        // −/＋を押された能力の要素番号(0始まり)を流す。
        // タブを開くまで非アクティブでAwakeが呼ばれないため、最初に参照したときに作る。
        public Observable<int> OnDecreaseButtonClicked => onDecreaseButtonClicked ??= Observable.Merge(
            statRows.Select((row, index) => row.OnDecreaseButtonClicked.Select(_ => index)).ToArray());
        public Observable<int> OnIncreaseButtonClicked => onIncreaseButtonClicked ??= Observable.Merge(
            statRows.Select((row, index) => row.OnIncreaseButtonClicked.Select(_ => index)).ToArray());
        public Observable<Unit> OnRevertButtonClicked => revertButton.OnClickAsObservable();
        public Observable<Unit> OnConfirmButtonClicked => confirmButton.OnClickAsObservable();

        public void Refresh(TrainingStatsViewDto dto)
        {
            remainingPointsText.text = dto.RemainingPoints.ToString();
            for (var index = 0; index < statRows.Length; index++)
            {
                statRows[index].Refresh(dto.Stats[index]);
            }

            // 足りないときも「確定する」は押せるままにし、押したらチケット不足のダイアログからショップへ移れるようにする。
            var shortage = dto.RequiredTickets - dto.TicketsBefore;
            ticketText.text = shortage > 0
                ? $"必要な育成チケット <b>{dto.RequiredTickets}枚</b>(所持 {dto.TicketsBefore})"
                : $"必要な育成チケット <b>{dto.RequiredTickets}枚</b>({dto.TicketsBefore} → {dto.TicketsAfter})";
            shortageText.gameObject.SetActive(shortage > 0);
            shortageText.text = $"あと{shortage}枚足りません";
            revertButton.interactable = dto.IsDirty;
            confirmButton.interactable = dto.IsDirty;
        }
    }
}
