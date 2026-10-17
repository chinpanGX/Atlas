using R3;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Training
{
    // 技・ステータスのタブ1つ。選択中の見た目(濃い地に白文字)は、ボタンの上に重ねた別のオブジェクトで出す。
    public sealed class TrainingTabButtonView : MonoBehaviour
    {
        [SerializeField] private CommonButton button;
        [SerializeField] private GameObject selected;

        public Observable<Unit> OnClicked => button.OnClickAsObservable();

        public void SetSelected(bool isSelected)
        {
            selected.SetActive(isSelected);
        }
    }
}
