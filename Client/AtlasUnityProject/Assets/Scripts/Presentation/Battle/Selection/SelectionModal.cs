using System.Collections.Generic;
using System.Linq;
using Atlas.Presentation.Party;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Battle
{
    // 選出画面。上に残り時間、中央に自分のパーティ(タップで選ぶ)、右に相手のパーティ、下に「けってい」。
    public sealed class SelectionModal : Modal
    {
        [SerializeField] private TextMeshProUGUI remainingTimeText;
        [SerializeField] private TextMeshProUGUI messageText;
        // 要素番号がSelectionViewDto.SelfPartyのインデックスに対応する。
        [SerializeField] private SelectionCandidateView[] candidateViews;
        [SerializeField] private PachimonInfoView selectedInfoView;
        [SerializeField] private SelectionOpponentView[] opponentViews;
        [SerializeField] private CommonButton confirmButton;

        // 押された候補のインデックス(SelectionViewDto.SelfParty)を流す。
        public Observable<int> OnCandidateClicked { get; private set; }
        public Observable<Unit> OnConfirmButtonClicked => confirmButton.OnClickAsObservable();

        private void Awake()
        {
            OnCandidateClicked = Observable.Merge(
                candidateViews.Select((view, index) => view.OnClicked.Select(_ => index)).ToArray());
        }

        public void Refresh(SelectionViewDto dto)
        {
            for (var index = 0; index < candidateViews.Length; index++)
            {
                if (index < dto.SelfParty.Count)
                {
                    candidateViews[index].Refresh(dto.SelfParty[index]);
                }
                else
                {
                    candidateViews[index].Hide();
                }
            }

            for (var index = 0; index < opponentViews.Length; index++)
            {
                if (index < dto.OpponentParty.Count)
                {
                    opponentViews[index].Refresh(dto.OpponentParty[index]);
                }
                else
                {
                    opponentViews[index].Hide();
                }
            }
        }

        public void ShowInfo(PachimonInfoDto info)
        {
            selectedInfoView.Refresh(info);
        }

        public void SetRemainingSeconds(int seconds)
        {
            remainingTimeText.text = $"のこり {seconds}秒";
        }

        // selectedIndexesは選んだ順の候補インデックス。選び終わった(selectionCount体)時だけ「けってい」を押せる。
        public void SetSelection(IReadOnlyList<int> selectedIndexes, int selectionCount, bool canConfirm)
        {
            for (var index = 0; index < candidateViews.Length; index++)
            {
                var order = IndexOf(selectedIndexes, index);
                candidateViews[index].SetOrder(order >= 0 ? order + 1 : null);
            }

            var remaining = selectionCount - selectedIndexes.Count;
            messageText.text = remaining > 0 ? $"たたかわせる パチモンを あと{remaining}体 えらんでください" : "この じゅんばんで たたかいます";
            confirmButton.interactable = canConfirm;
        }

        // 「けってい」を押した後、相手の選出が済むまで。選んだ順番の表示は残し、「けってい」は押せなくする。
        public void ShowWaitingForOpponent()
        {
            messageText.text = BattleMessageBuilder.WaitingForOpponentSelection;
            confirmButton.interactable = false;
        }

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
