using System.Collections.Generic;
using Atlas.Presentation.Common;
using Atlas.Presentation.Party;
using R3;
using TMPro;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Training
{
    // 育成画面。左に所持パチモンの一覧、右に選んだパチモンを出し、技とステータスをタブで切り替える。
    public sealed class TrainingPage : Page
    {
        [SerializeField] private PachimonListView pachimonListView;
        [SerializeField] private GameObject detailPanel;
        [SerializeField] private PachimonThumbnailView thumbnail;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private PachimonTypeBadgeView primaryTypeBadge;
        [SerializeField] private PachimonTypeBadgeView secondaryTypeBadge;
        [SerializeField] private TrainingTabButtonView movesTabButton;
        [SerializeField] private TrainingTabButtonView statsTabButton;
        [SerializeField] private TrainingMovesView movesView;
        [SerializeField] private TrainingStatsView statsView;

        // 押された所持パチモンのPlayerPachimonIdを流す。
        public Observable<string> OnPachimonClicked => pachimonListView.OnPachimonClicked;
        public Observable<TrainingTab> OnTabClicked { get; private set; }
        public TrainingMovesView MovesView => movesView;
        public TrainingStatsView StatsView => statsView;

        private void Awake()
        {
            OnTabClicked = Observable.Merge(
                movesTabButton.OnClicked.Select(_ => TrainingTab.Moves),
                statsTabButton.OnClicked.Select(_ => TrainingTab.Stats));
        }

        public void RefreshPachimonList(IReadOnlyList<PachimonDto> pachimons)
        {
            pachimonListView.Refresh(pachimons);
        }

        // 選んでいるパチモンに選択中の枠を、パーティに入っているパチモンに印を付ける。
        public void RefreshSelection(string selectedPachimonId, IReadOnlyCollection<string> partyPachimonIds)
        {
            pachimonListView.RefreshFrames(selectedPachimonId, partyPachimonIds);
        }

        public void RefreshPachimon(TrainingPachimonDto pachimon)
        {
            detailPanel.SetActive(true);
            thumbnail.Show(pachimon.PachimonId);
            nameText.text = pachimon.Name;
            primaryTypeBadge.Show(pachimon.PrimaryType);
            secondaryTypeBadge.Show(pachimon.SecondaryType);
        }

        // 所持が0体のときは右側ごと隠す。
        public void HidePachimon()
        {
            detailPanel.SetActive(false);
        }

        public void ShowTab(TrainingTab tab)
        {
            movesTabButton.SetSelected(tab == TrainingTab.Moves);
            statsTabButton.SetSelected(tab == TrainingTab.Stats);
            movesView.gameObject.SetActive(tab == TrainingTab.Moves);
            statsView.gameObject.SetActive(tab == TrainingTab.Stats);
        }
    }
}
