using System.Linq;
using R3;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.PartyEdit
{
    public sealed class PartyEditPage : Page
    {
        private const string EmptySlotLabel = "未編成";

        [SerializeField] private CommonButton backButton;
        [SerializeField] private CommonButton saveButton;
        // 要素番号がパーティのslot-1に対応する(slotはサーバー側と同じ1始まり、最大6体)。
        [SerializeField] private CommonButton[] slotButtons;

        public Observable<Unit> OnBackButtonClicked => backButton.OnClickAsObservable();
        public Observable<Unit> OnSaveButtonClicked => saveButton.OnClickAsObservable();

        // 押されたスロットのslot番号(1始まり)を流す。
        public Observable<int> OnSlotButtonClicked { get; private set; }

        private void Awake()
        {
            OnSlotButtonClicked = Observable.Merge(
                slotButtons.Select((button, index) => button.OnClickAsObservable().Select(_ => index + 1)).ToArray());
        }

        public void Refresh(PartyEditViewDto dto)
        {
            for (var index = 0; index < slotButtons.Length; index++)
            {
                var slotNo = index + 1;
                var slot = dto.Slots.FirstOrDefault(s => s.Slot == slotNo);
                slotButtons[index].SetTextInChildSafe(slot?.PachimonName ?? EmptySlotLabel);
            }
        }
    }
}
