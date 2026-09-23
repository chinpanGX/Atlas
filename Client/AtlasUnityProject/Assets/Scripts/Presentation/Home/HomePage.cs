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
        [SerializeField] private Button grantGemsButton;
        [SerializeField] private Button scoutButton;
        [SerializeField] private Button partyButton;
        [SerializeField] private Button battleButton;
        [SerializeField] private Button chatButton;

        // 動作確認用のジェム付与ボタン(正式なゲーム内機能ではない)。
        public Observable<Unit> OnGrantGemsButtonClicked => grantGemsButton.OnClickAsObservable();
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
