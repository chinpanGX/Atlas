using System.Collections.Generic;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Training
{
    // 技の枠に入れる技を、その種族が覚えられる技の一覧から選ぶ。選ぶと下に「いまの技 → 選んだ技」とチケットの変化を出す。
    public sealed class MoveReplaceModal : Modal
    {
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private RectTransform candidateContent;
        [SerializeField] private MoveCandidateView candidateTemplate;
        [SerializeField] private TextMeshProUGUI changeText;
        [SerializeField] private TextMeshProUGUI ticketText;
        [SerializeField] private CommonButton replaceButton;
        [SerializeField] private CommonButton cancelButton;

        private readonly List<MoveCandidateView> candidateViews = new();
        private readonly Subject<int> onCandidateClicked = new();
        private readonly CompositeDisposable candidateSubscriptions = new();
        private MoveReplaceViewDto dto;

        // 押された候補の並び順(0始まり)を流す。選べない候補は押せない。
        public Observable<int> OnCandidateClicked => onCandidateClicked;
        public Observable<Unit> OnReplaceButtonClicked => replaceButton.OnClickAsObservable();
        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();

        private void Awake()
        {
            candidateTemplate.gameObject.SetActive(false);
        }

        public void Refresh(MoveReplaceViewDto dto)
        {
            this.dto = dto;
            titleText.text = $"「{dto.CurrentMoveName}」と入れ替える技";
            countText.text = $"{dto.PachimonName}が覚えられる技 {dto.Candidates.Count}件";

            candidateSubscriptions.Clear();
            foreach (var view in candidateViews)
            {
                Destroy(view.gameObject);
            }
            candidateViews.Clear();

            for (var index = 0; index < dto.Candidates.Count; index++)
            {
                var view = Instantiate(candidateTemplate, candidateContent);
                view.gameObject.SetActive(true);
                view.Refresh(dto.Candidates[index]);
                var clickedIndex = index;
                view.OnClicked.Subscribe(_ => onCandidateClicked.OnNext(clickedIndex)).AddTo(candidateSubscriptions);
                candidateViews.Add(view);
            }

            RefreshSelection(null);
        }

        // selectedIndexは選んでいる候補の並び順。まだ選んでいなければnull。
        public void RefreshSelection(int? selectedIndex)
        {
            for (var index = 0; index < candidateViews.Count; index++)
            {
                candidateViews[index].SetSelected(index == selectedIndex);
            }

            var ticketsAfter = dto.TicketsBefore - dto.TicketCost;
            if (selectedIndex is { } selected)
            {
                changeText.text = $"{dto.CurrentMoveName} → <b>{dto.Candidates[selected].Name}</b>";
                ticketText.text = $"育成チケット {dto.TicketsBefore} → <b>{ticketsAfter}</b>({dto.TicketCost}枚使う)";
            }
            else
            {
                changeText.text = "入れ替える技を選んでください";
                ticketText.text = $"育成チケット {dto.TicketsBefore}(付け替え1回につき{dto.TicketCost}枚)";
            }

            replaceButton.interactable = selectedIndex is not null && ticketsAfter >= 0;
        }

        private void OnDestroy()
        {
            candidateSubscriptions.Dispose();
            onCandidateClicked.Dispose();
        }
    }
}
