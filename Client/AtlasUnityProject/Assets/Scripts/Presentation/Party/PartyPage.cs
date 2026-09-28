using System.Collections.Generic;
using R3;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;
using ZLinq;

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

        // 選択中の枠(selectedSlot)または一覧のパチモン(selectedPachimonId)に選択フレームを、
        // 編成中のパチモンに編成中フレームを付ける。未選択の側はnull。
        public void RefreshFrames(int? selectedSlot, string selectedPachimonId, IReadOnlyCollection<string> partyPachimonIds)
        {
            for (var index = 0; index < slotViews.Length; index++)
            {
                slotViews[index].SetSelected(index + 1 == selectedSlot);
            }

            pachimonListView.RefreshFrames(selectedPachimonId, partyPachimonIds);
        }

        public void RefreshPachimonInfo(PachimonInfoDto info)
        {
            pachimonInfoView.Refresh(info);
        }

        public void HidePachimonInfo()
        {
            pachimonInfoView.Hide();
        }

        // 保存中は戻るボタンを押せなくする。
        public void SetBackButtonInteractable(bool interactable)
        {
            backButton.interactable = interactable;
        }
    }
}
