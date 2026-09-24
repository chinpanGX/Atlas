using System.Collections.Generic;
using Atlas.Navigation;
using Atlas.Presentation.Party;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using ZLinq;

namespace Atlas.Presentation.Battle
{
    // 交代画面。左に選出3体、中央に選択中のパチモンの詳細(パーティ編成画面と同じPachimonInfoView)、
    // 下に「こうたいする」「もどる」。
    public sealed class SwitchSelectModal : ResultModal<SwitchSelectResult>
    {
        [SerializeField] private TextMeshProUGUI messageText;
        // 要素番号がSwitchCandidateDto.PartySlotに対応する。
        [SerializeField] private SwitchCandidateView[] candidateViews;
        [SerializeField] private PachimonInfoView pachimonInfoView;
        [SerializeField] private CommonButton confirmButton;
        [SerializeField] private CommonButton cancelButton;

        // 押された候補のPartySlotを流す。
        public Observable<int> OnCandidateClicked { get; private set; }
        public Observable<Unit> OnConfirmButtonClicked => confirmButton.OnClickAsObservable();
        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();

        protected override SwitchSelectResult CanceledResult => SwitchSelectResult.Canceled;

        private void Awake()
        {
            OnCandidateClicked = Observable.Merge(
                candidateViews.Select((view, index) => view.OnClicked.Select(_ => index)).ToArray());
        }

        public void Refresh(SwitchSelectViewDto dto)
        {
            messageText.text = dto.IsForced ? "次に出すパチモンを選んでください" : "交代するパチモンを選んでください";
            cancelButton.gameObject.SetActive(!dto.IsForced);

            for (var index = 0; index < candidateViews.Length; index++)
            {
                var candidate = FindCandidate(dto.Candidates, index);
                if (candidate is null)
                {
                    candidateViews[index].Hide();
                }
                else
                {
                    candidateViews[index].Refresh(candidate);
                }
            }
        }

        // 選択中の候補に枠を付けて詳細を表示し、交代できる候補の時だけ「こうたいする」を押せるようにする。
        public void Select(SwitchCandidateDto candidate)
        {
            for (var index = 0; index < candidateViews.Length; index++)
            {
                candidateViews[index].SetSelected(index == candidate.PartySlot);
            }

            pachimonInfoView.Refresh(candidate.Info);
            confirmButton.interactable = candidate.CanSwitchTo;
        }

        private static SwitchCandidateDto FindCandidate(IReadOnlyList<SwitchCandidateDto> candidates, int partySlot)
        {
            return candidates.FirstOrDefault(c => c.PartySlot == partySlot);
        }
    }
}
