using Atlas.Navigation;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas.Presentation.Battle
{
    // 投了する=true。キャンセル以外の閉じ方も投了しない扱い(CanceledResult=false)。
    public sealed class ForfeitConfirmModal : ResultModal<bool>
    {
        [SerializeField] private Button forfeitButton;
        [SerializeField] private Button cancelButton;

        public Observable<Unit> OnForfeitButtonClicked => forfeitButton.OnClickAsObservable();
        public Observable<Unit> OnCancelButtonClicked => cancelButton.OnClickAsObservable();
    }
}
