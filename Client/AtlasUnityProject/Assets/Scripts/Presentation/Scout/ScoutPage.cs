using System;
using System.Collections.Generic;
using Atlas.Presentation.Party;
using R3;
using TMPro;
using UIPackages.Runtime;
using UnityEngine;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Scout
{
    // スカウト画面。スカウト前は中央のスカウトボタン、スカウト後は下部に候補10体を並べ、
    // 選択中の候補のステータスを左、技を右に表示する。
    public sealed class ScoutPage : Page
    {
        [SerializeField] private TextMeshProUGUI bannerText;
        [SerializeField] private TextMeshProUGUI gemText;
        [SerializeField] private TextMeshProUGUI guideText;
        [SerializeField] private CommonButton scoutButton;
        [SerializeField] private CommonButton backButton;
        [SerializeField] private ScoutCandidateListView candidateListView;
        [SerializeField] private PachimonInfoView pachimonInfoView;

        public Observable<Unit> OnScoutButtonClicked => scoutButton.OnClickAsObservable();
        public Observable<Unit> OnBackButtonClicked => backButton.OnClickAsObservable();
        // 押された候補の並び順(0始まり)を流す。
        public Observable<int> OnCandidateClicked => candidateListView.OnCandidateClicked;

        public void SetBanner(string bannerName, int costPerRoll)
        {
            bannerText.text = $"{bannerName}(1回 {costPerRoll}ジェム)";
        }

        public void SetGems(int gems)
        {
            gemText.text = $"ジェム {gems}";
        }

        public void SetScoutButtonInteractable(bool interactable)
        {
            scoutButton.interactable = interactable;
        }

        public void SetBackButtonInteractable(bool interactable)
        {
            backButton.interactable = interactable;
        }

        public void ShowCandidates(IReadOnlyList<PachimonDto> candidates)
        {
            candidateListView.Refresh(candidates);
            scoutButton.gameObject.SetActive(false);
            guideText.gameObject.SetActive(true);
        }

        public void ClearCandidates()
        {
            candidateListView.Refresh(Array.Empty<PachimonDto>());
            scoutButton.gameObject.SetActive(true);
            guideText.gameObject.SetActive(false);
            pachimonInfoView.Hide();
        }

        public void SetSelectedCandidate(int? index)
        {
            candidateListView.SetSelected(index);
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
