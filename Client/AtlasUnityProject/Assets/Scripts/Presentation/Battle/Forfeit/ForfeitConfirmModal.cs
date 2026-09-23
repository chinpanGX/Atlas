using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityScreenNavigator.Runtime.Core.Modal;

namespace Atlas.Presentation.Battle
{
    public sealed class ForfeitConfirmModal : Modal
    {
        [SerializeField] private Button forfeitButton;
        [SerializeField] private Button cancelButton;

        public Observable<Unit> OnForfeitButtonClicked => forfeitButton.OnClickAsObservable();
        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();
    }
}
