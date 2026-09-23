using System.Collections.Generic;
using System.Linq;
using R3;
using UIPackages.Runtime;
using UnityEngine;

namespace Atlas.Presentation.Battle
{
    // 画面右下のコマンド。「たたかう」「こうたい」を並べたコマンドパネルと、技4つ+「もどる」の技パネルを
    // 切り替えて表示する(どちらか一方だけを表示する)。パネルの切り替え(たたかう→技パネル、もどる→
    // コマンドパネル)はこのView内で完結し、Presenterには技の選択と交代の要求だけを通知する。
    public sealed class CommandView : MonoBehaviour
    {
        [SerializeField] private GameObject commandPanel;
        [SerializeField] private GameObject movePanel;
        [SerializeField] private CommonButton fightButton;
        [SerializeField] private CommonButton switchButton;
        [SerializeField] private CommonButton backButton;
        // 要素番号がCommandDto.SlotNoに対応する。
        [SerializeField] private MoveCommandView[] moveCommands;

        private readonly CompositeDisposable disposables = new();

        // 押された技のSlotNoを流す。
        public Observable<int> OnMoveClicked { get; private set; }
        public Observable<Unit> OnSwitchClicked => switchButton.OnClickAsObservable();

        private void Awake()
        {
            OnMoveClicked = Observable.Merge(
                moveCommands.Select((view, index) => view.OnClicked.Select(_ => index)).ToArray());
            fightButton.OnClickAsObservable().Subscribe(_ => ShowMovePanel()).AddTo(disposables);
            backButton.OnClickAsObservable().Subscribe(_ => ShowCommandPanel()).AddTo(disposables);
            ShowCommandPanel();
        }

        private void OnDestroy()
        {
            disposables.Dispose();
        }

        public void ShowCommandPanel()
        {
            commandPanel.SetActive(true);
            movePanel.SetActive(false);
        }

        private void ShowMovePanel()
        {
            commandPanel.SetActive(false);
            movePanel.SetActive(true);
        }

        // SlotNoが無い(=対応する技が無い)枠のボタンは非表示にする。
        public void Refresh(IReadOnlyList<CommandDto> commands)
        {
            for (var slot = 0; slot < moveCommands.Length; slot++)
            {
                var command = commands.FirstOrDefault(c => c.SlotNo == slot);
                moveCommands[slot].gameObject.SetActive(command is not null);
                if (command is not null)
                {
                    moveCommands[slot].Refresh(command);
                }
            }
        }

        // 行動(技・交代)を受け付けるかどうか。「もどる」はパネルを戻すだけなので常に押せる。
        public void SetInteractable(bool interactable)
        {
            fightButton.interactable = interactable;
            switchButton.interactable = interactable;
            foreach (var move in moveCommands)
            {
                move.SetInteractable(interactable);
            }
        }
    }
}
