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

        // PlayerPachimonId → セル。入れ替えのたびにセルを作り直さず、フレームだけ切り替えるために持つ。
        private readonly Dictionary<string, PachimonCellView> cells = new();
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
            foreach (var cell in cells.Values)
            {
                Destroy(cell.gameObject);
            }
            cells.Clear();

            foreach (var pachimon in pachimons)
            {
                var cell = Instantiate(cellTemplate, content);
                cell.gameObject.SetActive(true);
                cell.Refresh(pachimon);
                cell.SetFrames(isInParty: false, isSelected: false);
                var playerPachimonId = pachimon.PlayerPachimonId;
                cell.OnClicked.Subscribe(_ => onPachimonClicked.OnNext(playerPachimonId)).AddTo(cellSubscriptions);
                cells[playerPachimonId] = cell;
            }
        }

        // selectedPachimonIdは一覧で選択中のもの(未選択ならnull)。
        public void RefreshFrames(string selectedPachimonId, IReadOnlyCollection<string> partyPachimonIds)
        {
            var partyIds = new HashSet<string>(partyPachimonIds);
            foreach (var pair in cells)
            {
                pair.Value.SetFrames(partyIds.Contains(pair.Key), pair.Key == selectedPachimonId);
            }
        }

        private void OnDestroy()
        {
            cellSubscriptions.Dispose();
            onPachimonClicked.Dispose();
        }
    }
}
