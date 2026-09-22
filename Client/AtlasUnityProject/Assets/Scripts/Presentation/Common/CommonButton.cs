using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Common
{
    public sealed class CommonButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TextMeshProUGUI labelText;

        public Observable<Unit> OnClick => button.OnClickAsObservable();

        public string Label
        {
            get => labelText.text;
            set => labelText.text = value;
        }

        public bool Interactable
        {
            get => button.interactable;
            set => button.interactable = value;
        }
    }
}
