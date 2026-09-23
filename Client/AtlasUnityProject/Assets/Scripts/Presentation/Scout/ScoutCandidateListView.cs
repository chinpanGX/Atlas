using System.Collections.Generic;
using Atlas.Presentation.Party;
using R3;
using UnityEngine;

namespace Atlas.Presentation.Scout
{
    // 画面下部の候補一覧(横スクロール)。セルはパーティ編成の所持一覧と同じPachimonCellViewを複製して使う。
    public sealed class ScoutCandidateListView : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private PachimonCellView cellTemplate;

        private readonly List<PachimonCellView> cells = new();
        private readonly Subject<int> onCandidateClicked = new();
        private readonly CompositeDisposable cellSubscriptions = new();

        // 押されたセルの並び順(0始まり)を流す。
        public Observable<int> OnCandidateClicked => onCandidateClicked;

        private void Awake()
        {
            cellTemplate.gameObject.SetActive(false);
        }

        public void Refresh(IReadOnlyList<PachimonDto> candidates)
        {
            cellSubscriptions.Clear();
            foreach (var cell in cells)
            {
                Destroy(cell.gameObject);
            }
            cells.Clear();

            for (var index = 0; index < candidates.Count; index++)
            {
                var cell = Instantiate(cellTemplate, content);
                cell.gameObject.SetActive(true);
                cell.Refresh(candidates[index]);
                cell.SetFrames(isInParty: false, isSelected: false);
                var clickedIndex = index;
                cell.OnClicked.Subscribe(_ => onCandidateClicked.OnNext(clickedIndex)).AddTo(cellSubscriptions);
                cells.Add(cell);
            }
        }

        public void SetSelected(int? selectedIndex)
        {
            for (var index = 0; index < cells.Count; index++)
            {
                cells[index].SetFrames(isInParty: false, isSelected: index == selectedIndex);
            }
        }

        private void OnDestroy()
        {
            cellSubscriptions.Dispose();
            onCandidateClicked.Dispose();
        }
    }
}
