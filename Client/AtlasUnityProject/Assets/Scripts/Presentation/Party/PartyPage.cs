using System.Collections.Generic;
using System.Linq;
using R3;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Party
{
    // パーティ編成画面。左に編成中のパーティ(上からslot1)、中央に所持パチモン一覧、
    // 右にタップしたパチモンの詳細を表示する。
    public sealed class PartyPage : Page
    {
        // 要素番号がslot-1に対応する(slotは1始まり、最大6体)。
        [SerializeField] private PartySlotView[] slotViews;
        [SerializeField] private PachimonListView pachimonListView;
        [SerializeField] private PachimonInfoView pachimonInfoView;
        [SerializeField] private CommonButton backButton;

        // 押されたパーティ枠のslot番号(1始まり)を流す。
        public Observable<int> OnSlotClicked { get; private set; }
        // 押された所持パチモンのPlayerPachimonIdを流す。
        public Observable<string> OnPachimonClicked => pachimonListView.OnPachimonClicked;
        public Observable<Unit> OnBackButtonClicked => backButton.OnClickAsObservable();

        private void Awake()
        {
            OnSlotClicked = Observable.Merge(
                slotViews.Select((view, index) => view.OnClicked.Select(_ => index + 1)).ToArray());
        }

        public void RefreshParty(PartyDto party)
        {
            for (var index = 0; index < slotViews.Length; index++)
            {
                var slotNo = index + 1;
                var slot = party.Slots.FirstOrDefault(s => s.Slot == slotNo);
                slotViews[index].Refresh(slot?.Pachimon);
            }
        }

        public void RefreshPachimonList(IReadOnlyList<PachimonDto> pachimons)
        {
            pachimonListView.Refresh(pachimons);
        }

        public void RefreshPachimonInfo(PachimonInfoDto info)
        {
            pachimonInfoView.Refresh(info);
        }

        public void HidePachimonInfo()
        {
            pachimonInfoView.Hide();
        }
    }
}
