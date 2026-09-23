using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Home
{
    public sealed class HomePage : Page
    {
        [SerializeField] private TextMeshProUGUI nicknameText;
        [SerializeField] private TextMeshProUGUI gemsText;
        [SerializeField] private Button scoutButton;
        [SerializeField] private Button partyButton;
        [SerializeField] private Button battleButton;
        [SerializeField] private Button chatButton;

        public Observable<Unit> OnScoutButtonClicked => scoutButton.OnClickAsObservable();
        public Observable<Unit> OnPartyButtonClicked => partyButton.OnClickAsObservable();
        public Observable<Unit> OnBattleButtonClicked => battleButton.OnClickAsObservable();
        public Observable<Unit> OnChatButtonClicked => chatButton.OnClickAsObservable();

        public void Refresh(HomeViewDto dto)
        {
            nicknameText.text = dto.PlayerId;
            gemsText.text = dto.Gems.ToString();
        }
    }
}
