using System.Linq;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Battle
{
    public sealed class SwitchSelectModal : Modal
    {
        [SerializeField] private TextMeshProUGUI messageText;
        // 要素番号がSwitchCandidateDto.PartySlotに対応する。
        [SerializeField] private SwitchCandidateView[] candidateViews;
        [SerializeField] private CommonButton cancelButton;

        // 押された候補のPartySlotを流す。
        public Observable<int> OnCandidateClicked { get; private set; }
        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();

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
                var candidate = dto.Candidates.FirstOrDefault(c => c.PartySlot == index);
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
    }
}
