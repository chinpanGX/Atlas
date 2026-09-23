using System.Collections.Generic;
using R3;
using UnityEngine;

namespace Atlas.Presentation.Party
{
    // 画面中央の所持パチモン一覧(ScrollRect + GridLayoutGroup)。所持数は可変なので、
    // 非アクティブで置いてあるcellTemplateをRefreshのたびに必要数だけ複製する。
    public sealed class PachimonListView : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private PachimonCellView cellTemplate;

        private readonly List<PachimonCellView> cells = new();
        private readonly Subject<string> onPachimonClicked = new();
        private readonly CompositeDisposable cellSubscriptions = new();

        // 押されたセルのPlayerPachimonIdを流す。
        public Observable<string> OnPachimonClicked => onPachimonClicked;

        private void Awake()
        {
            cellTemplate.gameObject.SetActive(false);
        }

        public void Refresh(IReadOnlyList<PachimonDto> pachimons)
        {
            cellSubscriptions.Clear();
            foreach (var cell in cells)
            {
                Destroy(cell.gameObject);
            }
            cells.Clear();

            foreach (var pachimon in pachimons)
            {
                var cell = Instantiate(cellTemplate, content);
                cell.gameObject.SetActive(true);
                cell.Refresh(pachimon);
                var playerPachimonId = pachimon.PlayerPachimonId;
                cell.OnClicked.Subscribe(_ => onPachimonClicked.OnNext(playerPachimonId)).AddTo(cellSubscriptions);
                cells.Add(cell);
            }
        }

        private void OnDestroy()
        {
            cellSubscriptions.Dispose();
            onPachimonClicked.Dispose();
        }
    }
}
