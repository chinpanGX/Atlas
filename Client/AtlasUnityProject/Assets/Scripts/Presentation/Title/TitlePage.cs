using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Page;

namespace Atlas.Presentation.Title
{
    public sealed class TitlePage : Page
    {
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button startButton;

        public Observable<Unit> OnStartButtonClicked => startButton.OnClickAsObservable();

        public void Refresh(TitleViewDto dto)
        {
            messageText.text = dto.Message;
        }
    }
}
